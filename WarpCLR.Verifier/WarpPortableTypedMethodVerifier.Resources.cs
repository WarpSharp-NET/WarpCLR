using System.Reflection.Emit;

namespace WarpCLR.Verifier;

internal sealed partial class WarpPortableTypedMethodVerifier
{
    private int MaximumValueWords()
    {
        int maximum = 6;
        foreach (string identity in argumentTypes.Concat(method.LocalTypes)) { maximum = Math.Max(maximum, types.Get(identity).WordCount); }
        foreach (WarpPortableMethodGraphInstruction instruction in method.Instructions)
        {
            OpCode operation = instruction.OpCode;
            if (instruction.Type is { } type && (operation == OpCodes.Ldobj || operation == OpCodes.Unbox_Any || operation == OpCodes.Ldelem))
            {
                maximum = Math.Max(maximum, types.Get(type).WordCount);
            }

            if (instruction.Field is { } field && (operation == OpCodes.Ldfld || operation == OpCodes.Ldsfld))
            {
                maximum = Math.Max(maximum, types.Get(fields[field].FieldType).WordCount);
            }

            if (instruction.Method is { } target && (operation == OpCodes.Call || operation == OpCodes.Callvirt || operation == OpCodes.Newobj))
            {
                string result = operation == OpCodes.Newobj ? types.Get(methods[target].SourceMethod.DeclaringType!).Identity : methods[target].ReturnType;
                maximum = Math.Max(maximum, types.Get(result).WordCount);
            }
        }

        return maximum;
    }
}
