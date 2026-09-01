using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
[assembly: AssemblyMetadata("WarpCIL.Manifest", """{"contract":"warpcil/0.1","producer":"WarpCLR.Tests","producerVersion":"0.1.0","entries":[{"type":"WarpCLR.Tests.TestKernels","method":"ManifestMap","execution":"map","parameterRoles":["input","scalar"],"capabilities":["warp.core.scalar/0.1","warp.core.parallel/0.1","warp.core.buffers/0.1","warp.memory.scoped/0.1"],"graphHash":"73EA6961D7383318BED4980E9B8EC8489C4322FC765E251A05001A5C08B9FDA0"},{"type":"WarpCLR.Tests.TestKernels","method":"ManifestReduction","execution":"reduce-wrapping-sum","parameterRoles":["input","scalar"],"capabilities":["warp.core.scalar/0.1","warp.core.parallel/0.1","warp.core.buffers/0.1","warp.memory.scoped/0.1"],"graphHash":"A84B49A435675B1CE2FC775B9E5495C50E436AF4AFBDB507D3521A4582873CF2"}],"hostImports":[],"extensions":[]}""")]
