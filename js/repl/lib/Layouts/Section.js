import { L, Record, declare, Union } from "../../fable-core/Types.js";
import { Common$002EHelpers$$$classes as Common$0024002EHelpers$0024$0024$0024classes, Modifier$$$parseModifiers as Modifier$0024$0024$0024parseModifiers } from "../Fulma/Common.js";
import { fold } from "../../fable-core/List.js";
import { createObj } from "../../fable-core/Util.js";
const createElement = React.createElement;
export const Option = declare(function Option(tag, name, ...fields) {
  Union.call(this, tag, name, ...fields);
}, Union);
export const Options = declare(function Options(arg1, arg2, arg3, arg4) {
  this.Props = arg1;
  this.CustomClass = arg2;
  this.Spacing = arg3;
  this.Modifiers = arg4;
}, Record);
export function Options$$$get_Empty() {
  return new Options(L(), null, null, L());
}
export function section(options, children) {
  const parseOptions = function parseOptions(result, opt) {
    switch (opt.tag) {
      case 2:
        {
          return new Options(result.Props, result.CustomClass, "is-medium", result.Modifiers);
        }

      case 3:
        {
          return new Options(result.Props, result.CustomClass, "is-large", result.Modifiers);
        }

      case 1:
        {
          const customClass = opt.fields[0];
          return new Options(result.Props, customClass, result.Spacing, result.Modifiers);
        }

      case 4:
        {
          const modifiers = opt.fields[0];
          return new Options(result.Props, result.CustomClass, result.Spacing, Modifier$0024$0024$0024parseModifiers(modifiers));
        }

      default:
        {
          const props = opt.fields[0];
          return new Options(props, result.CustomClass, result.Spacing, result.Modifiers);
        }
    }
  };

  const opts = fold(parseOptions, Options$$$get_Empty(), options);
  const classes = Common$0024002EHelpers$0024$0024$0024classes("section", L(opts.CustomClass, L(opts.Spacing, opts.Modifiers)), L());
  return createElement("section", createObj(L(classes, opts.Props), 1), ...children);
}