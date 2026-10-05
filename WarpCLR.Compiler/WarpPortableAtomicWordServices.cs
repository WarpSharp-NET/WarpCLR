namespace WarpCLR.Compiler;

internal static partial class WarpPortableAtomicWordServices
{
    // These helpers require the retained SC controller grant. All overlapping accessors must use this same grant.
    // Target owner/view and the disjoint result slice are admitted by the immutable typed program, not by user CIL.
    // The grant is retained across every helper quantum and through inline result delivery, then released by SC CAS.
    public static uint OperateArena(uint[] arena, uint scheduler, uint controller, uint dispatch, uint epoch,
        uint word, uint byteShift, uint width, uint operation, uint comparandLow, uint comparandHigh,
        uint valueLow, uint valueHigh, uint result)
    {
        uint status = Validate(arena, arena, scheduler, controller, dispatch, epoch, word, byteShift, width, operation, result, 0);
        if (status != 0) { return status; }
        return Operate(arena, arena, word, byteShift, width, operation, comparandLow, comparandHigh, valueLow, valueHigh, result);
    }

    public static uint OperateState(uint[] state, uint[] arena, uint scheduler, uint controller, uint dispatch, uint epoch,
        uint word, uint byteShift, uint width, uint operation, uint comparandLow, uint comparandHigh,
        uint valueLow, uint valueHigh, uint result)
    {
        uint status = Validate(state, arena, scheduler, controller, dispatch, epoch, word, byteShift, width, operation, result, 1);
        if (status != 0) { return status; }
        return Operate(state, arena, word, byteShift, width, operation, comparandLow, comparandHigh, valueLow, valueHigh, result);
    }

    private static uint Validate(uint[] storage, uint[] arena, uint scheduler, uint controller, uint dispatch, uint epoch,
        uint word, uint byteShift, uint width, uint operation, uint result, uint ownState)
    {
        uint status = WarpPortableSchedulerServices.ValidateAtomicGrant(arena, scheduler, controller, dispatch, epoch);
        if (status != 0) { return status; }
        if (byteShift > 3 || width != 4 && width != 8 || operation == 0 || operation > 11) { return 3; }
        uint length = (uint)storage.Length;
        uint tail = (byteShift + width - 1) >> 2;
        if (word >= length || tail >= length - word || result > (uint)arena.Length || (uint)arena.Length - result < 2) { return 2; }
        uint metadataEnd = scheduler + arena[scheduler + WarpPortableSchedulerLayout.DescriptorWords];
        if (result < metadataEnd || word < 64 || ownState == 0 &&
            (word < metadataEnd && scheduler <= word + tail || result <= word + tail && word < result + 2)) { return 3; }
        return 0;
    }

    private static uint Operate(uint[] storage, uint[] arena, uint word, uint byteShift, uint width, uint operation,
        uint comparandLow, uint comparandHigh, uint valueLow, uint valueHigh, uint result)
    {
        uint low = ReadBytes(storage, word, byteShift, 4);
        uint high = width == 8 ? ReadBytes(storage, word, byteShift + 4, 4) : 0;
        uint outputLow = low, outputHigh = high;
        uint replacementLow = valueLow, replacementHigh = width == 8 ? valueHigh : 0;
        bool write = operation != 1 && operation != 10;
        if (operation == 3) { write = low == comparandLow && (width == 4 || high == comparandHigh); }
        else if (operation is 5 or 6 or 7)
        {
            uint deltaLow = operation == 6 ? 1 : operation == 7 ? 0xFFFFFFFF : valueLow;
            uint deltaHigh = operation == 6 ? 0 : operation == 7 ? 0xFFFFFFFF : valueHigh;
            replacementLow = low + deltaLow;
            replacementHigh = width == 8 ? high + deltaHigh + (replacementLow < low ? 1u : 0u) : 0;
            outputLow = replacementLow; outputHigh = replacementHigh;
        }
        else if (operation == 8) { replacementLow = low & valueLow; replacementHigh = high & valueHigh; }
        else if (operation == 9) { replacementLow = low | valueLow; replacementHigh = high | valueHigh; }
        else if (operation is 2 or 11) { outputLow = replacementLow; outputHigh = replacementHigh; }
        if (write)
        {
            WriteBytes(storage, word, byteShift, replacementLow);
            if (width == 8) { WriteBytes(storage, word, byteShift + 4, replacementHigh); }
        }
        arena[result] = outputLow; arena[result + 1] = outputHigh;
        return 0;
    }

    private static uint ReadBytes(uint[] storage, uint word, uint byteShift, uint count)
    {
        uint value = 0;
        for (uint index = 0; index < count; index++)
        {
            uint address = byteShift + index;
            uint item = (storage[word + (address >> 2)] >> ((int)(address & 3) * 8)) & 0xFF;
            value |= item << ((int)index * 8);
        }
        return value;
    }

    private static uint WriteBytes(uint[] storage, uint word, uint byteShift, uint value)
    {
        for (uint index = 0; index < 4; index++)
        {
            uint address = byteShift + index;
            uint destination = word + (address >> 2);
            uint shift = (address & 3) * 8;
            uint mask = 0xFFu << (int)shift;
            storage[destination] = (storage[destination] & ~mask) | (((value >> ((int)index * 8)) & 0xFF) << (int)shift);
        }
        return 0;
    }
}
