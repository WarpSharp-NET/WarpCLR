using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        // Physical LLVM partitioning adds no logical node, state word, source
        // event or operational charge. State/private SSA and quantum_ptr retain
        // their exact existing ownership across these ordinary native calls.
        private const int MaximumNodesPerNativePartition = 32;

        private string BodyCallingConvention => backend == WarpBackendKind.SPIRV ? "spir_func " : string.Empty;

        private string QuantumPointerType => backend == WarpBackendKind.AMDGPU ? "ptr addrspace(5)" : "ptr";

        private void AppendFunctionDispatch()
        {
            Line("dispatch:");
            AppendDispatchFrame();
            string function = LoadFrame(WarpLogicalMachineLayout.FrameFunctionOffset);
            Line($"  switch i32 {function}, label %invalid_state [");
            for (int body = 0; body <= layout.Kernel.Functions.Count; body++)
            {
                Line($"    i32 {N(body)}, label %body_{N(body)}");
            }
            Line("  ]");
            for (int body = 0; body <= layout.Kernel.Functions.Count; body++)
            {
                Line($"body_{N(body)}:");
                string proceed = Assign($"call {BodyCallingConvention}i1 @warp_body_{N(body)}({BodyParameters()})");
                Line($"  br i1 {proceed}, label %dispatch, label %done");
            }
            AppendInvalidState();
        }

        private string BodyParameters()
        {
            var parameters = new List<string>
            {
                "ptr addrspace(1) %state", $"{QuantumPointerType} %quantum_ptr", "i32 %warp_max_depth",
                "i32 %physical_max_depth", "i64 %frame_area", "i64 %input_index", "i64 %stride",
            };
            for (int index = 0; index < layout.Kernel.InputBufferCount; index++)
            {
                parameters.Add($"ptr addrspace(1) %warp_input_{N(index)}");
            }
            for (int index = 0; index < layout.Kernel.ScalarArgumentCount; index++)
            {
                parameters.Add($"i32 %warp_scalar_{N(index)}");
            }
            if (layout.RequiresManagedMemory)
            {
                parameters.AddRange(["ptr addrspace(1) %warp_heap", "i32 %warp_heap_words"]);
            }
            return string.Join(", ", parameters);
        }

        private void AppendFunctionBody(int function)
        {
            WarpLogicalMachineNode[] body = layout.Nodes.Where(node => node.Function == function).ToArray();
            if (body.Length <= MaximumNodesPerNativePartition)
            {
                AppendNodePartition(function, body, $"warp_body_{N(function)}", split: false);
                return;
            }
            WarpLogicalMachineNode[][] partitions = body.Chunk(MaximumNodesPerNativePartition).ToArray();
            AppendPartitionRouter(function, partitions);
            for (int partition = 0; partition < partitions.Length; partition++)
            {
                AppendNodePartition(function, partitions[partition], $"warp_body_{N(function)}_partition_{N(partition)}", split: true);
            }
        }

        private void AppendPartitionRouter(int function, WarpLogicalMachineNode[][] partitions)
        {
            AppendBodyEntry(function, $"warp_body_{N(function)}");
            string pc = LoadFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset);
            Line($"  switch i32 {pc}, label %invalid_state [");
            for (int partition = 0; partition < partitions.Length; partition++)
            {
                foreach (WarpLogicalMachineNode node in partitions[partition])
                { Line($"    i32 {N(node.ProgramCounter)}, label %partition_{N(partition)}"); }
            }
            Line("  ]");
            for (int partition = 0; partition < partitions.Length; partition++)
            {
                Line($"partition_{N(partition)}:");
                string proceed = Assign($"call {BodyCallingConvention}i1 @warp_body_{N(function)}_partition_{N(partition)}({BodyParameters()})");
                Line($"  br i1 {proceed}, label %dispatch, label %done");
            }
            AppendInvalidState();
            AppendBodyEnd();
        }

        private void AppendNodePartition(int function, WarpLogicalMachineNode[] body, string name, bool split)
        {
            AppendBodyEntry(function, name);
            string pc = LoadFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset);
            if (split)
            {
                // The immutable body router owns validity. A new PC outside
                // this shard returns to it within the same native quantum.
                Line($"  switch i32 {pc}, label %changed_function [");
                foreach (WarpLogicalMachineNode node in body)
                { Line($"    i32 {N(node.ProgramCounter)}, label %node_{N(node.ProgramCounter)}"); }
                Line("  ]");
            }
            else { AppendProgramCounterSwitch(pc, body); }
            AppendInvalidState();
            foreach (WarpLogicalMachineNode node in body) { AppendNode(node); }
            AppendBodyEnd();
        }

        private void AppendBodyEntry(int function, string name)
        {
            Line($"define internal {BodyCallingConvention}i1 @{name}({BodyParameters()}) #1 {{");
            Line("entry:");
            Line("  br label %dispatch");
            Line("dispatch:");
            AppendDispatchFrame();
            string current = LoadFrame(WarpLogicalMachineLayout.FrameFunctionOffset);
            string same = Assign($"icmp eq i32 {current}, {N(function)}");
            Line($"  br i1 {same}, label %dispatch_pc, label %changed_function");
            Line("changed_function:");
            Line("  ret i1 true");
            Line("dispatch_pc:");
        }

        private void AppendBodyEnd()
        {
            Line("done:");
            Line("  ret i1 false");
            Line("}");
        }
    }
}
