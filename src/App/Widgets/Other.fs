namespace Widgets

module General =

    open Fable.Helpers.React
    open Fable.Helpers.React.Props
    open Fulma
    open Fulma.FontAwesome

    type ResetState =
        | Default
        | Confirm

    type Model =
        { ResetState : ResetState }

    type Msg =
        | AskReset
        | ConfirmReset
        | CancelReset
        | Share

    type ExternalMessage =
        | NoOp
        | Reset
        | Share

    let init () =
        { ResetState = Default }

    let update msg model =
        match msg with
        | AskReset ->
            { model with ResetState = Confirm }, NoOp

        | ConfirmReset ->
            { model with ResetState = Default }, Reset

        | CancelReset ->
            { model with ResetState = Default }, NoOp

        | Msg.Share ->
            model, ExternalMessage.Share


    let view (model: Model) dispatch =
        let content =
            match model.ResetState with
            | Default ->
                div [ ]
                    [ Field.div [ Field.HasAddons ]
                        [ Control.div [ ]
                            [ Button.button [ Button.Color IsDark
                                              Button.OnClick (fun _ -> dispatch AskReset) ]
                                [ Icon.faIcon [ ]
                                    [ Fa.icon Fa.I.Refresh ] ] ]
                          Control.div [ Control.IsExpanded ]
                            [ Button.button [ Button.OnClick (fun _ -> dispatch AskReset)
                                              Button.IsText
                                              Button.IsFullWidth ]
                                [ Text.span [ ]
                                    [ str "Click here to reset" ] ] ] ]
                      Field.div [ Field.HasAddons ]
                        [ Control.div [ ]
                            [ Button.button [ Button.Color IsDark
                                              Button.OnClick (fun _ -> dispatch  Msg.Share) ]
                                [ Icon.faIcon [ ]
                                    [ Fa.icon Fa.I.Share ] ] ]
                          Control.div [ Control.IsExpanded ]
                            [ Button.button [ Button.OnClick (fun _ -> dispatch  Msg.Share)
                                              Button.IsText
                                              Button.IsFullWidth ]
                                [ Text.span [ ]
                                    [ str "Share" ] ] ] ] ]
            | Confirm ->
                Field.div [ ]
                    [ Help.help [ Help.Color IsWarning ]
                        [ str "Please, confirm to reset" ]
                      Field.div [ Field.HasAddons ]
                        [
                          Control.p [ ]
                            [ Button.button [ Button.OnClick (fun _ -> dispatch ConfirmReset)
                                              Button.Color IsSuccess ]
                                [ Icon.faIcon [ ]
                                    [ Fa.icon Fa.I.Check ]
                                  Text.span [ ]
                                    [ str "Confirm" ] ] ]
                          Control.p [ ]
                            [ Button.button [ Button.OnClick (fun _ -> dispatch CancelReset)
                                              Button.Color IsDanger ]
                                [ Icon.faIcon [ ]
                                    [ Fa.icon Fa.I.Times ]
                                  Text.span [ ]
                                    [ str "Cancel" ] ] ] ] ]

        Content.content [ ]
            [ content ]

module Options =

    open Fable.Helpers.React
    open Fable.Helpers.React.Props
    open Fulma

    type Model =
        { FscOptimize: bool
          AggressiveInline: bool
        }

    type Msg =
        | ToggleFscOptimize
        | ToggleAggressiveInline

    let init () =
        { FscOptimize = false
          AggressiveInline = false
        }

    let update msg model =
        match msg with
        | ToggleFscOptimize ->
            { model with FscOptimize = not model.FscOptimize }
        | ToggleAggressiveInline ->
            { model with AggressiveInline = not model.AggressiveInline }

    let checkbox txt value dispatch msg =
        Checkbox.checkbox [ ]
            [ Checkbox.input
                [ Common.Props [ Checked value
                                 OnChange (fun _ -> dispatch msg) ] ]
              str (" " + txt) ]


    let view (model: Model) dispatch =
        Content.content [ ]
            [ checkbox "FSC Optimize" model.FscOptimize dispatch ToggleFscOptimize
              checkbox "Aggressive Inline" model.AggressiveInline dispatch ToggleAggressiveInline
            ]

module About =

    open Fable.Helpers.React
    open Fable.Helpers.React.Props
    open Fulma
    open Fable.Repl.Shared

    let view =
        Content.content [ ]
            [ str ("Version: " + Literals.VERSION)
              br [ ]
              a [ Href "https://github.com/fable-compiler/repl2/issues/new" ]
                [ Text.span [ Modifiers [ Modifier.TextTransform TextTransform.Italic ] ]
                    [ str "Found a bug ?" ] ] ]
