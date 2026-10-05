using WarpCLR.Compiler;

namespace WarpCLR.Tests.Production;

internal sealed partial class WarpPortableCollectiveTests
{
    private static void Check(uint type, uint operation, uint overflow, uint kind, ulong[] inputs,
        bool generated, bool minimum, int residents, bool parallel = false)
    {
        WarpPortableCollectivePlan plan = Plan(type, operation, overflow, kind, (uint)inputs.Length);
        var driver = new WarpPortableCollectiveDriver(plan, inputs, generated, minimum, residents);
        WarpPortableCollectiveOracle.Outcome[] expected = WarpPortableCollectiveOracle.Evaluate(type, operation, overflow, kind, inputs);
        uint[] immutable = plan.Metadata.ToArray();
        uint status = driver.Run(parallel);
        int firstFault = Array.FindIndex(expected, static value => value.FaultStart >= 0);
        Assert.AreEqual(firstFault < 0 ? 3u : 4u, status, $"type={type}, operation={operation}, checked={overflow}, kind={kind}, count={inputs.Length}");
        CheckPreparedOutputs(driver, expected);
        if (firstFault >= 0)
        {
            Assert.AreEqual(2u, driver.Arena[driver.Descriptor + 29]);
            Assert.AreEqual((uint)firstFault, driver.Arena[driver.Descriptor + 31]);
            CheckFaultRange(driver, driver.Arena[driver.Descriptor + 30], expected[firstFault]);
            Assert.AreNotEqual(driver.Generation, driver.Arena[driver.Descriptor + 41]);
        }
        else
        {
            Assert.AreEqual(driver.Generation, driver.Arena[driver.Descriptor + 41]);
            Assert.AreEqual(0u, driver.Control(nameof(WarpPortableCollectiveServices.AcknowledgeResults), driver.Generation));
        }
        CheckImmutable(driver, immutable);
    }

    private static void CheckPreparedOutputs(WarpPortableCollectiveDriver driver, WarpPortableCollectiveOracle.Outcome[] expected)
    {
        for (uint i = 0; i < expected.Length; i++)
        {
            uint id = driver.Arena[driver.Arena[driver.Descriptor + 16] + i];
            uint state = driver.Arena[driver.Descriptor + 13] + id * 12;
            Assert.AreEqual(driver.Generation, driver.Arena[state + 5]);
            Assert.AreEqual(2u, driver.Arena[state]);
            Assert.AreEqual(expected[i].Bits, driver.Arena[state + 1] | ((ulong)driver.Arena[state + 2] << 32), $"output={i}");
            Assert.AreEqual(expected[i].FaultStart < 0 ? 0u : 2u, driver.Arena[state + 3]);
            if (expected[i].FaultStart >= 0) { CheckFaultRange(driver, driver.Arena[state + 4], expected[i]); }
        }
    }

    private static void CheckFaultRange(WarpPortableCollectiveDriver driver, uint id, WarpPortableCollectiveOracle.Outcome expected)
    {
        Assert.IsGreaterThan(0u, id);
        Assert.IsLessThan(driver.Plan.Nodes, id);
        uint node = driver.Arena[driver.Descriptor + 12] + id * 8;
        Assert.AreEqual((uint)expected.FaultStart, driver.Arena[node + 3]);
        Assert.AreEqual((uint)expected.FaultEnd, driver.Arena[node + 4]);
    }

    private static void CheckImmutable(WarpPortableCollectiveDriver driver, uint[] original)
    {
        for (int i = 0; i < 22; i++) { Assert.AreEqual(original[i], driver.Arena[driver.Descriptor + (uint)i]); }
        for (int i = 32; i <= 40; i++) { Assert.AreEqual(original[i], driver.Arena[driver.Descriptor + (uint)i]); }
        for (int i = 43; i < 64; i++) { Assert.AreEqual(original[i], driver.Arena[driver.Descriptor + (uint)i]); }
        uint[] offsets = [12, 14, 15, 16, 17];
        uint[] lengths = [driver.Plan.Nodes * 8, driver.Plan.Nodes - 1, driver.Plan.Levels * 2, driver.Plan.Outputs, driver.Arena[driver.Descriptor + 4]];
        for (int part = 0; part < offsets.Length; part++)
        {
            uint start = driver.Arena[driver.Descriptor + offsets[part]];
            for (uint i = 0; i < lengths[part]; i++)
            {
                Assert.AreEqual(original[start - driver.Descriptor + i], driver.Arena[start + i]);
            }
        }
    }
}
