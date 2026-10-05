using System.Collections.Immutable;

namespace WarpCLR.Compiler;

internal sealed partial class WarpPortableSourceHeapSchema
{
    private sealed partial class Builder
    {
        private ImmutableArray<WarpPortableSourceExceptionType> ExceptionTypes() => typeIds.OrderBy(pair => pair.Value)
            .Select(pair => ExceptionType(pair.Value, sources[pair.Key])).ToImmutableArray();

        private static WarpPortableSourceExceptionType ExceptionType(uint id, Type source)
        {
            // These are the closest captured builtin base kinds, not an execution of any constructor.
            // A generated operation-specific factory must still bind the actual constructor/data contract.
            if (typeof(ArgumentNullException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.ArgumentNull, 0x80004003); }
            if (typeof(ArgumentOutOfRangeException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.ArgumentOutOfRange, 0x80131502); }
            if (typeof(ArgumentException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.Argument, 0x80070057); }
            if (typeof(TypeInitializationException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.TypeInitialization, 0x80131534); }
            if (typeof(NullReferenceException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.NullReference, 0x80004003); }
            if (typeof(IndexOutOfRangeException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.IndexOutOfRange, 0x80131508); }
            if (typeof(OverflowException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.Overflow, 0x80131516); }
            if (typeof(DivideByZeroException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.DivideByZero, 0x80020012); }
            if (typeof(ArithmeticException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.Arithmetic, 0x80070216); }
            if (typeof(ArrayTypeMismatchException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.ArrayTypeMismatch, 0x80131503); }
            if (typeof(InvalidCastException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.InvalidCast, 0x80004002); }
            if (typeof(InvalidOperationException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.InvalidOperation, 0x80131509); }
            if (typeof(OutOfMemoryException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.OutOfMemory, 0x8007000E); }
            if (typeof(StackOverflowException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.StackOverflow, 0x800703E9); }
            if (typeof(SystemException).IsAssignableFrom(source)) { return new(id, WarpPortableSourceExceptionKind.System, 0x80131501); }
            return typeof(Exception).IsAssignableFrom(source) ? new(id, WarpPortableSourceExceptionKind.Exception, 0x80131500) : new(id, 0, 0);
        }
    }
}
