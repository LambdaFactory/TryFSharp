module Fable.Repl.Main

open Fable.Core.JsInterop
open Fable.Import
open Fable.PowerPack
open Fulma
open Fulma.FontAwesome
open Fulma.Extensions
open Elmish
open Thoth.Elmish
open Shared
open Editor
open Mouse

type ISavedState =
    abstract code: string
    abstract html: string
    abstract css: string

let private Worker(): Browser.Worker = importDefault "worker-loader!../Worker/Worker.fsproj"
let private loadState(_key: string): ISavedState = importMember "./js/util.js"
let private saveState(_key: string, _code: string, _html: string, _cssCode : string): unit = importMember "./js/util.js"
let private updateQuery(_fsharpCode : string, _htmlCode : string, _cssCode : string): unit = importMember "./js/util.js"

type IEditor = Monaco.Editor.IStandaloneCodeEditor

type State =
    | Loading
    | Idle
    | Compiling
    | Compiled

[<RequireQualifiedAccess>]
type OutputTab =
    | Code
    | Live

[<RequireQualifiedAccess>]
type CodeTab =
    | FSharp
    | Html
    | Css

type DragTarget =
    | NoTarget
    | PanelSplitter

type EditorCollapse =
    | BothExtended
    | HtmlOnly
    | FSharpOnly

type Model =
    { FSharpEditor: IEditor
      Worker: ObservableWorker<WorkerAnswer>
      State: State
      IFrameUrl : string
      OutputTab : OutputTab
      CodeTab : CodeTab
      CodeES2015: string
      FSharpCode : string
      FSharpErrors : ResizeArray<Monaco.Editor.IMarkerData>
      HtmlCode: string
      CssCode: string
      DragTarget : DragTarget
      PanelSplitRatio : float
      Sidebar : Sidebar.Model
      IsProblemsPanelExpanded : bool
      Logs : ConsolePanel.Log list }

[<RequireQualifiedAccess>]
type CompileResult =
    | Ok of string
    | Errors of Fable.Repl.Error[]
    | Error of string

type Msg =
    | SetFSharpEditor of IEditor
    | LoadSuccess
    | LoadFail
    | Reset
    | UrlHashChange
    | MarkEditorErrors of Fable.Repl.Error[]
    | StartCompile of string option
    | EndCompile of CompileResult
    | UpdateStats of CompileStats
    | ShareableUrlReady of unit
    | SetOutputTab of OutputTab
    | SetCodeTab of CodeTab
    | ToggleProblemsPanel
    | SetIFrameUrl of string
    | PanelDragStarted
    | PanelDrag of Position
    | PanelDragEnded
    | MouseUp
    | MouseMove of Mouse.Position
    | AddConsoleLog of string
    | AddConsoleError of string
    | AddConsoleWarn of string
    | SidebarMsg of Sidebar.Msg
    | ChangeFsharpCode of string
    | ChangeHtmlCode of string
    | ChangeCssCode of string
    | UpdateQueryFailed of exn
    | RefreshIframe
    | FetchCode of fsharp : string * html : CodeInfo * css : CodeInfo
    | FetchGist of id: string
    | FetchCodeSuccess of fsharp : string * html : string * css : string
    | FetchCodeError of exn

let private addLog log (model : Model) =
    { model with Logs =
                    if model.Logs.Length >= Literals.MAX_LOGS_LENGTH then
                        model.Logs.Tail @ [log]
                    else
                        model.Logs @ [log] }

let private generateHtmlUrl (model: Model) jsCode =
    saveState(Literals.STORAGE_KEY, model.FSharpCode, model.HtmlCode, model.CssCode)
    Generator.generateHtmlBlobUrl model.HtmlCode model.CssCode jsCode

let private clamp min max value =
    if value >= max
    then max
    elif value <= min
    then min
    else value

let private parseEditorCode (worker: ObservableWorker<_>) (model: Monaco.Editor.IModel) =
    let content = model.getValue (Monaco.Editor.EndOfLinePreference.TextDefined, true)
    ParseCode content |> worker.Post

let private showGlobalErrorToast msg =
    Toast.message msg
    |> Toast.title "Failed to compiled"
    |> Toast.position Toast.BottomRight
    |> Toast.icon Fa.I.Exclamation
    |> Toast.noTimeout
    |> Toast.withCloseButton
    |> Toast.error

let getCodeFromUrl (fsharpUrl, htmlInfo, cssInfo) =
    promise {
        let url = "samples/" + fsharpUrl
        let! fsharpRes = Fetch.fetch url []
        let! fsharpCode = fsharpRes.text()

        let! htmlCode =
            promise {
                match htmlInfo with
                | Default ->
                    return Generator.defaultHtmlCode
                | Url url ->
                    match! Fetch.tryFetch ("samples/" + url) [] with
                    | Ok htmlRes -> return! htmlRes.text()
                    | Error _ -> return Generator.defaultHtmlCode
            }

        let! cssCode =
            promise {
                match cssInfo with
                | Default ->
                    return ""
                | Url url ->
                    match! Fetch.tryFetch ("samples/" + url) [] with
                    | Ok cssRes -> return! cssRes.text()
                    | Error _ -> return ""
            }

        return fsharpCode, htmlCode, cssCode
    }

let getGist id =
    let tryFileWithExtension ext (keys: string seq) (filesObj: obj): string option =
        keys |> Seq.tryFind (fun k -> k.EndsWith(ext))
        |> Option.map (fun key -> filesObj?(key)?content)

    promise {
        let url = "https://api.github.com/gists/" + id
        let! res = Fetch.fetch url []
        let! json = res.json()
        let files: obj[] = json?files
        let fileKeys = JS.Object.keys(files)
        let fsharp = tryFileWithExtension ".fs" fileKeys files |> Option.get
        let html = tryFileWithExtension ".html" fileKeys files
        let css = tryFileWithExtension ".css" fileKeys files
        return fsharp, defaultArg html Generator.defaultHtmlCode, defaultArg css ""
    }

let fetchCodeCmd state (fsharpUrl, htmlInfo, cssInfo) =
    match state with
    // Do nothing if we're compiling to prevent getting a different result
    | Compiling -> Cmd.none
    | _ -> Cmd.ofPromise getCodeFromUrl (fsharpUrl, htmlInfo, cssInfo) FetchCodeSuccess FetchCodeError

let fetchGistCmd id =
    Cmd.ofPromise getGist id FetchCodeSuccess FetchCodeError

let update msg (model : Model) =
    match msg with
    | LoadSuccess ->
        // Parse code every X seconds.
        let md = model.FSharpEditor.getModel()
        let obs =
            createObservable(fun trigger ->
                model.FSharpEditor.getModel().onDidChangeContent(fun _ -> trigger md) |> ignore)
        debounce 1000 obs
        |> Observable.add (parseEditorCode model.Worker)
        obs.Trigger md

        let browserAdviceCommand =
            if not ReactDeviceDetect.exports.isChrome
                && not ReactDeviceDetect.exports.isSafari then

                Toast.message "We recommend using Chrome or Safari, for best performance"
                |> Toast.icon Fa.I.Info
                |> Toast.position Toast.BottomRight
                |> Toast.timeout (System.TimeSpan.FromSeconds 5.)
                |> Toast.dismissOnClick
                |> Toast.info

            else
                Cmd.none

        { model with State = Idle }, Cmd.batch [ Cmd.ofMsg (StartCompile None)
                                                 browserAdviceCommand ]

    | LoadFail ->
        let msg = "Assemblies couldn't be loaded. Some firewalls prevent download of binary files, please check."
        { model with State = Idle }, showGlobalErrorToast msg

    | SetFSharpEditor ed -> { model with FSharpEditor = ed }, Cmd.none

    | ToggleProblemsPanel ->
        { model with IsProblemsPanelExpanded = not model.IsProblemsPanelExpanded }, Cmd.none

    | Reset ->
        Browser.window.localStorage.removeItem(Literals.STORAGE_KEY)
        let saved = loadState(Literals.STORAGE_KEY)
        { model with FSharpCode = saved.code
                     HtmlCode = saved.html
                     CssCode = saved.css
                     CodeES2015 = ""
                     IFrameUrl = ""
                     Logs = [] }, Router.modifyUrl Router.Home

    | UrlHashChange ->
        let parsed = loadState(Literals.STORAGE_KEY)
        { model with FSharpCode = parsed.code; HtmlCode = parsed.html }, Cmd.ofMsg (StartCompile (Some parsed.code))

    | MarkEditorErrors errors ->
        { model with FSharpErrors = mapErrorToMarker errors }, Cmd.none

    | StartCompile code ->
        if model.State <> Compiling then
            let code =
                match code with
                | Some code -> code
                | None -> model.FSharpCode
            CompileCode(code, model.Sidebar.Options.Optimize) |> model.Worker.Post
            { model with State = Compiling }, Cmd.none
        else
         model, Cmd.none

    | EndCompile result ->
        match result with
        | CompileResult.Ok codeES2015 ->
            { model with CodeES2015 = codeES2015
                         State = Compiled
                         FSharpErrors = ResizeArray [||]
                         Logs = [ ConsolePanel.Log.Separator ] }, Cmd.batch [ Cmd.performFunc (generateHtmlUrl model) codeES2015 SetIFrameUrl
                                                                              Toast.message "Compiled successfuly"
                                                                              |> Toast.position Toast.BottomRight
                                                                              |> Toast.icon Fa.I.Check
                                                                              |> Toast.dismissOnClick
                                                                              |> Toast.success ]

        | CompileResult.Errors errors ->
            { model with State = Compiled
                         FSharpErrors = mapErrorToMarker errors }, Toast.message "Failed to compiled"
                                                                    |> Toast.position Toast.BottomRight
                                                                    |> Toast.icon Fa.I.Exclamation
                                                                    |> Toast.dismissOnClick
                                                                    |> Toast.error

        | CompileResult.Error msg ->
            { model with State = Compiled }, showGlobalErrorToast msg

    | SetIFrameUrl newUrl ->
        { model with IFrameUrl = newUrl }, Cmd.none

    | SetOutputTab newTab ->
        { model with OutputTab = newTab }, Cmd.none

    | SetCodeTab newTab ->
        { model with CodeTab = newTab }, Cmd.none

    | MouseUp ->
        let cmd =
            match model.DragTarget with
            | NoTarget -> Cmd.none
            | PanelSplitter ->
                Cmd.ofMsg PanelDragEnded
        model, cmd

    | MouseMove position ->
        let cmd =
            match model.DragTarget with
            | NoTarget -> Cmd.none
            | PanelSplitter ->
                Cmd.ofMsg (PanelDrag position)
        model, cmd

    | PanelDragStarted ->
        { model with DragTarget = PanelSplitter }, Cmd.none

    | PanelDragEnded ->
        { model with DragTarget = NoTarget }, Cmd.none

    | PanelDrag position ->
        let offset =
            if model.Sidebar.IsExpanded then 250. else 0.
        let splitRatio =
            position
            |> (fun p -> p.X - offset)
            |> (fun w -> w / (Browser.window.innerWidth - offset))
            |> clamp 0.2 0.8
        // printfn "PANELDRAG: x %f offset %f innerWidth %f splitRatio %f"
        //     position.X offset Browser.window.innerWidth splitRatio
        { model with PanelSplitRatio = splitRatio }, Cmd.none

    | SidebarMsg msg ->
        let (subModel, cmd, externalMsg) = Sidebar.update msg model.Sidebar
        let newModel, extraCmd =
            match externalMsg with
            | Sidebar.NoOp -> model, Cmd.none
            | Sidebar.FetchCode (fsharpCode, htmlCode, cssCode) ->
                model, fetchCodeCmd model.State (fsharpCode, htmlCode, cssCode)
            | Sidebar.Share ->
                model, Cmd.ofFunc updateQuery (model.FSharpCode, model.HtmlCode, model.CssCode) ShareableUrlReady UpdateQueryFailed
            | Sidebar.Reset ->
                model, Router.newUrl Router.Reset

        { newModel with Sidebar = subModel }, Cmd.batch [ Cmd.map SidebarMsg cmd
                                                          extraCmd ]

    | ChangeFsharpCode newCode ->
        { model with FSharpCode = newCode }, Cmd.none

    | ChangeHtmlCode newCode ->
        { model with HtmlCode = newCode }, Cmd.none

    | ChangeCssCode newCode ->
        { model with CssCode = newCode }, Cmd.none

    | ShareableUrlReady () ->
        model, Toast.message "Copy it from the address bar"
                |> Toast.title "Shareable link is ready"
                |> Toast.position Toast.BottomRight
                |> Toast.icon Fa.I.InfoCircle
                |> Toast.timeout (System.TimeSpan.FromSeconds 5.)
                |> Toast.info

    | UpdateQueryFailed exn ->
        Browser.console.error exn
        model, Toast.message "An error occured when updating the URL"
                |> Toast.icon Fa.I.Warning
                |> Toast.position Toast.BottomRight
                |> Toast.warning

    | UpdateStats stats ->
        model, Cmd.ofMsg (SidebarMsg (Sidebar.UpdateStats stats))

    | RefreshIframe ->
        { model with Logs = [ ConsolePanel.Log.Separator ] }, Cmd.performFunc (Generator.generateHtmlBlobUrl model.HtmlCode model.CssCode) model.CodeES2015 SetIFrameUrl

    | AddConsoleLog content ->
        model
        |> addLog (ConsolePanel.Log.Info content), Cmd.none

    | AddConsoleError content ->
        model
        |> addLog (ConsolePanel.Log.Error content), Cmd.none

    | AddConsoleWarn content ->
        model
        |> addLog (ConsolePanel.Log.Warn content), Cmd.none

    | FetchCode (fsharpCode, htmlCode, cssCode) ->
        model, fetchCodeCmd model.State (fsharpCode, htmlCode, cssCode)

    | FetchGist id ->
        model, fetchGistCmd id

    | FetchCodeSuccess (fsharpCode, htmlCode, cssCode) ->
        let cmd =
            match model.State with
            | Loading -> Cmd.none
            // Trigger a new compilation if not loading
            | _ -> Cmd.ofMsg (StartCompile (Some fsharpCode))
        { model with FSharpCode = fsharpCode
                     HtmlCode = htmlCode
                     CssCode = cssCode }, cmd

    | FetchCodeError error ->
        Browser.console.error error
        model, Cmd.none

let workerCmd (worker : ObservableWorker<_>)=
    let handler dispatch =
        worker
        |> Observable.add (function
            | Loaded ->
                LoadSuccess |> dispatch
            | LoadFailed -> LoadFail |> dispatch
            | ParsedCode errors -> MarkEditorErrors errors |> dispatch
            | CompilationFailed (errors, stats) ->
                CompileResult.Errors errors |> EndCompile |> dispatch
                UpdateStats stats |> dispatch
            | CompilationSucceed (jsCode, stats) ->
                CompileResult.Ok jsCode |> EndCompile |> dispatch
                UpdateStats stats |> dispatch
            | CompilerCrashed msg ->
                CompileResult.Error msg |> EndCompile |> dispatch
            // Do nothing, these will be handled by .PostAndAwaitResponse
            | FoundTooltip _ -> ()
            | FoundCompletions _ -> ()
            | FoundDeclarationLocation _ -> ()
        )
    [ handler ]

let init () =
    let worker = ObservableWorker(Worker(), WorkerAnswer.Decoder)

    let saved = loadState(Literals.STORAGE_KEY)
    let sidebarModel, sidebarCmd = Sidebar.init ()
    let cmd = Cmd.batch [
                Cmd.ups MouseUp
                Cmd.move MouseMove
                Cmd.iframeMessage
                    { MoveCtor = MouseMove
                      UpCtor = MouseUp
                      ConsoleLogCor = AddConsoleLog
                      ConsoleWarnCor = AddConsoleWarn
                      ConsoleErrorCor = AddConsoleError }
                Cmd.map SidebarMsg sidebarCmd
                workerCmd worker ]

    { State = Loading
      FSharpEditor = Unchecked.defaultof<IEditor>
      Worker = worker
      IFrameUrl = ""
      OutputTab = OutputTab.Live
      CodeTab = CodeTab.FSharp
      CodeES2015 = ""
      FSharpCode = saved.code
      FSharpErrors = ResizeArray [||]
      HtmlCode = saved.html
      CssCode = saved.css
      DragTarget = NoTarget
      PanelSplitRatio = 0.5
      Sidebar = sidebarModel
      IsProblemsPanelExpanded = true
      Logs = [] }, cmd

open Fable.Helpers.React
open Fable.Helpers.React.Props

let private numberToPercent number =
    string (number * 100.) + "%"

let private fontSizeClass =
        function
        | 11. -> "is-small"
        | 14. -> "is-medium"
        | 17. -> "is-large"
        | _ -> "is-medium"

let private htmlEditorOptions (fontSize : float) (fontFamily : string) =
    jsOptions<Monaco.Editor.IEditorConstructionOptions>(fun o ->
        let minimapOptions =  jsOptions<Monaco.Editor.IEditorMinimapOptions>(fun oMinimap ->
            oMinimap.enabled <- Some false
        )
        o.language <- Some "html"
        o.fontSize <- Some fontSize
        o.theme <- Some "vs-dark"
        o.minimap <- Some minimapOptions
        o.fontFamily <- Some fontFamily
        o.fontLigatures <- Some (fontFamily = "Fira Code")
    )

let private cssEditorOptions (fontSize : float) (fontFamily : string) =
    jsOptions<Monaco.Editor.IEditorConstructionOptions>(fun o ->
        let minimapOptions =  jsOptions<Monaco.Editor.IEditorMinimapOptions>(fun oMinimap ->
            oMinimap.enabled <- Some false
        )
        o.language <- Some "css"
        o.fontSize <- Some fontSize
        o.theme <- Some "vs-dark"
        o.minimap <- Some minimapOptions
        o.fontFamily <- Some fontFamily
        o.fontLigatures <- Some (fontFamily = "Fira Code")
    )

let private fsharpEditorOptions (fontSize : float) (fontFamily : string) =
    jsOptions<Monaco.Editor.IEditorConstructionOptions>(fun o ->
        let minimapOptions = jsOptions<Monaco.Editor.IEditorMinimapOptions>(fun oMinimap ->
            oMinimap.enabled <- Some false
        )
        o.language <- Some "fsharp"
        o.fontSize <- Some fontSize
        o.theme <- Some "vs-dark"
        o.minimap <- Some minimapOptions
        o.fontFamily <- Some fontFamily
        o.fontLigatures <- Some (fontFamily = "Fira Code")
    )

let private editorTabs (activeTab : CodeTab) dispatch =
    Tabs.tabs [ Tabs.IsCentered
                Tabs.Size Size.IsMedium
                Tabs.IsToggle ]
        [ Tabs.tab [ Tabs.Tab.IsActive (activeTab = CodeTab.FSharp)
                     Tabs.Tab.Props [
                        OnClick (fun _ -> SetCodeTab CodeTab.FSharp |> dispatch)
                     ] ]
            [ a [ ] [ str "F#" ] ]
          Tabs.tab [ Tabs.Tab.IsActive (activeTab = CodeTab.Html)
                     Tabs.Tab.Props [
                         OnClick (fun _ -> SetCodeTab CodeTab.Html |> dispatch)
                     ] ]
            [ a [ ] [ str "HTML" ] ]
          Tabs.tab [ Tabs.Tab.IsActive (activeTab = CodeTab.Css)
                     Tabs.Tab.Props [
                         OnClick (fun _ -> SetCodeTab CodeTab.Css |> dispatch)
                     ] ]
            [ a [ ] [ str "CSS" ] ] ]

let private problemsPanel (isExpanded : bool) (errors : ResizeArray<Monaco.Editor.IMarkerData>) (currentTab : CodeTab) dispatch =
    let bodyDisplay =
        if isExpanded then
            ""
        else
            "is-hidden"

    let headerIcon =
        if isExpanded then
            Fa.I.AngleDown
        else
            Fa.I.AngleUp

    let title =
        if errors.Count = 0 then
            span [ ]
                [ str "Problems" ]
        else
            span [ ]
                [ str "Problems: "
                  Text.span [ Props [ Style [ MarginLeft ".5rem" ] ] ]
                    [ str (string errors.Count ) ] ]

    div [ Class "scrollable-panel is-problem" ]
        [ div [ Class "scrollable-panel-header"
                OnClick (fun _ -> dispatch ToggleProblemsPanel) ]
            [ div [ Class "scrollable-panel-header-icon" ]
                [ Icon.faIcon [ ]
                    [ Fa.faLg
                      Fa.icon headerIcon ] ]
              div [ Class "scrollable-panel-header-title" ]
                [ title ]
              div [ Class "scrollable-panel-header-icon" ]
                [ Icon.faIcon [ ]
                    [ Fa.faLg
                      Fa.icon headerIcon ] ] ]

          div [ Class ("scrollable-panel-body " + bodyDisplay) ]
            [ for error in errors do
                match error.severity with
                | Monaco.MarkerSeverity.Error
                | Monaco.MarkerSeverity.Warning ->
                    let (icon, colorClass) =
                        match error.severity with
                        | Monaco.MarkerSeverity.Error -> Fa.I.TimesCircle, ofColor IsDanger
                        | Monaco.MarkerSeverity.Warning -> Fa.I.ExclamationTriangle, ofColor IsWarning
                        | _ -> failwith "Should not happen", ofColor NoColor

                    yield div [ Class ("scrollable-panel-body-row " + colorClass)
                                Data("tooltip-content", error.message)
                                OnClick (fun _ ->
                                    if currentTab <> CodeTab.FSharp then
                                        SetCodeTab CodeTab.FSharp |> dispatch
                                    ReactEditor.Dispatch.cursorMove "fsharp_cursor_jump" error
                                ) ]
                            [ Icon.faIcon [ Icon.Size IsSmall
                                            Icon.CustomClass colorClass ]
                                [ Fa.icon icon ]
                              span [ Class "scrollable-panel-body-row-description" ]
                                [ str error.message ]
                              span [ Class "scrollable-panel-body-row-position" ]
                                [ str "("
                                  str (string error.startLineNumber)
                                  str ","
                                  str (string error.startColumn)
                                  str ")" ] ]
                | _ -> () ] ]

let private registerCompileCommand dispatch (editor : Monaco.Editor.IStandaloneCodeEditor) (monacoModule : Monaco.IExports) =
    let triggerCompile () = StartCompile None |> dispatch
    editor.addCommand(monacoModule.KeyMod.Alt ||| int Monaco.KeyCode.Enter, triggerCompile, "") |> ignore
    editor.addCommand(monacoModule.KeyMod.CtrlCmd ||| int Monaco.KeyCode.KEY_S, triggerCompile, "") |> ignore

let private editorArea model dispatch =
    div [ Class "vertical-panel"
          Style [ Width (numberToPercent model.PanelSplitRatio)
                  Position "relative" ] ]
        [ editorTabs model.CodeTab dispatch
          // Html editor
          ReactEditor.editor [ ReactEditor.Options (htmlEditorOptions
                                                        model.Sidebar.Options.FontSize
                                                        model.Sidebar.Options.FontFamily)
                               ReactEditor.Value model.HtmlCode
                               ReactEditor.IsHidden (model.CodeTab <> CodeTab.Html)
                               ReactEditor.CustomClass (fontSizeClass model.Sidebar.Options.FontSize)
                               ReactEditor.OnChange (ChangeHtmlCode >> dispatch)
                               ReactEditor.EditorDidMount (registerCompileCommand dispatch) ]
          // Css editor
          ReactEditor.editor [ ReactEditor.Options (cssEditorOptions
                                                        model.Sidebar.Options.FontSize
                                                        model.Sidebar.Options.FontFamily)
                               ReactEditor.Value model.CssCode
                               ReactEditor.IsHidden (model.CodeTab <> CodeTab.Css)
                               ReactEditor.CustomClass (fontSizeClass model.Sidebar.Options.FontSize)
                               ReactEditor.OnChange (ChangeCssCode >> dispatch)
                               ReactEditor.EditorDidMount (registerCompileCommand dispatch) ]
          // F# editor
          ReactEditor.editor [ ReactEditor.Options (fsharpEditorOptions
                                                        model.Sidebar.Options.FontSize
                                                        model.Sidebar.Options.FontFamily)
                               ReactEditor.Value model.FSharpCode
                               ReactEditor.IsHidden (model.CodeTab <> CodeTab.FSharp)
                               ReactEditor.OnChange (ChangeFsharpCode >> dispatch)
                               ReactEditor.Errors model.FSharpErrors
                               ReactEditor.EventId "fsharp_cursor_jump"
                               ReactEditor.CustomClass (fontSizeClass model.Sidebar.Options.FontSize)
                               ReactEditor.EditorDidMount (fun editor monacoModule ->
                                if not (isNull editor) then
                                    dispatch (SetFSharpEditor editor)

                                    // Because we have access to the monacoModule here,
                                    // register the different provider needed for F# editor
                                    let getTooltip line column lineText =
                                        async {
                                            let! res = model.Worker.PostAndAwaitResponse(GetTooltip(line, column, lineText))
                                            match res with
                                            | FoundTooltip lines -> return lines
                                            | _ -> return [||]
                                        }

                                    let tooltipProvider = Editor.createTooltipProvider getTooltip
                                    monacoModule.languages.registerHoverProvider("fsharp", tooltipProvider) |> ignore

                                    let getDeclarationLocation uri line column lineText =
                                        async {
                                            let! res = model.Worker.PostAndAwaitResponse(GetDeclarationLocation(line, column, lineText))
                                            match res with
                                            | FoundDeclarationLocation res ->
                                                // printfn "FoundDeclarationLocation %A" res
                                                return res |> Option.map (fun (line1, col1, line2, col2) ->
                                                    uri, line1, col1, line2, col2)
                                            | _ -> return None
                                        }

                                    let editorUri = editor.getModel().uri
                                    let definitionProvider = Editor.createDefinitionProvider (getDeclarationLocation editorUri)
                                    monacoModule.languages.registerDefinitionProvider("fsharp", definitionProvider) |> ignore

                                    let getCompletion line column lineText =
                                        async {
                                            let! res = model.Worker.PostAndAwaitResponse(GetCompletions(line, column, lineText))
                                            match res with
                                            | FoundCompletions lines -> return lines
                                            | _ -> return [||]
                                        }

                                    let completionProvider = Editor.createCompletionProvider getCompletion
                                    monacoModule.languages.registerCompletionItemProvider("fsharp", completionProvider) |> ignore

                                    registerCompileCommand dispatch editor monacoModule
                               ) ]
          problemsPanel model.IsProblemsPanelExpanded model.FSharpErrors model.CodeTab dispatch ]


let private outputTabs (activeTab : OutputTab) dispatch =
    Tabs.tabs [ Tabs.IsCentered
                Tabs.Size Size.IsMedium
                Tabs.IsToggle ]
        [ Tabs.tab [ Tabs.Tab.IsActive (activeTab = OutputTab.Live)
                     Tabs.Tab.Props [
                         OnClick (fun _ -> SetOutputTab OutputTab.Live |> dispatch)
                     ] ]
            [ a [ ] [ str "Live sample" ] ]
          Tabs.tab [ Tabs.Tab.IsActive (activeTab = OutputTab.Code)
                     Tabs.Tab.Props [
                         OnClick (fun _ -> SetOutputTab OutputTab.Code |> dispatch)
                     ] ]
            [ a [ ] [ str "Code" ] ] ]

let private toggleDisplay cond =
    if cond then "" else "is-hidden"

let private viewIframe isShown url =
    iframe [ Src url
             Class (toggleDisplay isShown) ]
        [ ]

let private viewCodeEditor (model: Model) =
    let fontFamily = model.Sidebar.Options.FontFamily
    let options = jsOptions<Monaco.Editor.IEditorConstructionOptions>(fun o ->
                        let minimapOptions = jsOptions<Monaco.Editor.IEditorMinimapOptions>(fun oMinimap ->
                            oMinimap.enabled <- Some false
                        )
                        o.language <- Some "javascript"
                        o.fontSize <- Some model.Sidebar.Options.FontSize
                        o.theme <- Some "vs-dark"
                        o.minimap <- Some minimapOptions
                        o.readOnly <- Some true
                        o.fontFamily <- Some fontFamily
                        o.fontLigatures <- Some (fontFamily = "Fira Code")
                    )

    ReactEditor.editor [ ReactEditor.Options options
                         ReactEditor.Value model.CodeES2015
                         ReactEditor.IsHidden (model.OutputTab <> OutputTab.Code)
                         ReactEditor.CustomClass (fontSizeClass model.Sidebar.Options.FontSize) ]

let private outputArea model dispatch =
    let content =
        match model.State with
        | Compiling | Compiled ->
            [ outputTabs model.OutputTab dispatch
              div [ Class "output-content" ]
                [ viewIframe (model.OutputTab = OutputTab.Live) model.IFrameUrl
                  viewCodeEditor model
                  ConsolePanel.view model.Logs ] ]
        | _ ->
            [ br [ ]
              Text.div [ Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ]
                         Props [ Style [ Width "100%" ] ] ]
                [ Heading.h4 [ Heading.IsSubtitle ]
                    [ str "You need to compile an application first" ] ] ]

    div [ Class "output-container"
          Style [ Width (numberToPercent (1. - model.PanelSplitRatio)) ] ]
        content

let private actionArea (state : State) dispatch =
    let compileIcon =
        if state = State.Compiling then
            [ Fa.icon Fa.I.Spinner
              Fa.spin ]
        else
            [ Fa.icon Fa.I.Play ]

    let collapsed =
        div [ Class "actions-area" ]
            [ div [ Class "action-button" ]
                [ Button.button [ Button.IsOutlined
                                  Button.OnClick (fun _ -> dispatch (StartCompile None)) ]
                    [ Icon.faIcon [ Icon.Size IsSmall ]
                        compileIcon
                      span [ ]
                        [ str "Compile" ] ] ]
              div [ Class "action-button" ]
                [ Button.button [ Button.IsOutlined
                                  Button.OnClick (fun _ -> dispatch RefreshIframe) ]
                    [ Icon.faIcon [ Icon.Size IsSmall ]
                        [ Fa.icon Fa.I.Refresh ]
                      span [ ]
                        [ str "Refresh" ] ] ] ]

    let expanded =
        div [ Class "actions-area" ]
            [ div [ Class "action-button" ]
                [ Button.button [ Button.IsOutlined
                                  Button.OnClick (fun _ -> dispatch (StartCompile None)) ]
                    [ Icon.faIcon [ Icon.Size IsLarge ]
                        compileIcon ] ]
              div [ Class "action-button" ]
                [ Button.button [ Button.IsOutlined
                                  Button.OnClick (fun _ -> dispatch RefreshIframe) ]
                    [ Icon.faIcon [ Icon.Size IsLarge ]
                        [ Fa.icon Fa.I.Refresh ] ] ] ]

    (collapsed, expanded)

let view (model: Model) dispatch =
    Elmish.React.Common.lazyView2
        (fun model dispatch ->
            let isDragging =
                match model.DragTarget with
                | PanelSplitter -> true
                | NoTarget -> false
            div [ classList [ "is-unselectable", isDragging ] ]
                [ PageLoader.pageLoader [ PageLoader.Color IsPrimary
                                          PageLoader.IsActive (model.State = Loading) ]
                                        [ div [ Class "title has-text-centered"; Style [FontSize "1.2em"] ]
                                            [ p [] [str "We are getting everything ready for you"]
                                              p [] [str "While you wait, check out "; a [Href "http://fable.io/fableconf/#planning"; Target "_blank"] [str "the agenda for FableConf"]; str "!" ]
                                              br []
                                              p []
                                                [ str "Trouble loading the repl? "
                                                  a [ Router.href Router.Reset
                                                      Style [ TextDecoration "underline" ] ] [ str "Click here"]
                                                  str " to reset." ] ] ]

                  div [ Class "page-content" ]
                    [ Sidebar.view model.Sidebar (actionArea model.State dispatch) (SidebarMsg >> dispatch)
                      div [ Class "main-content" ]
                        [ editorArea model dispatch
                          div [ Class "horizontal-resize"
                                OnMouseDown (fun ev ->
                                    // This prevents text selection in Safari
                                    ev.preventDefault()
                                    dispatch PanelDragStarted) ]
                              [ ]
                          outputArea model dispatch ] ] ]

        )
        model
        dispatch
