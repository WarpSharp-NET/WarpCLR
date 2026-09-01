using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
[assembly: AssemblyMetadata("WarpCIL.Manifest", """{"contract":"warpcil/0.1","producer":"WarpCLR.Tests","producerVersion":"0.1.0","entries":[{"type":"WarpCLR.Tests.TestKernels","method":"ManifestMap","parameterRoles":["input","scalar"],"capabilities":["warp.core.scalar/0.1","warp.core.parallel/0.1","warp.core.buffers/0.1","warp.memory.scoped/0.1"],"graphHash":"73EA6961D7383318BED4980E9B8EC8489C4322FC765E251A05001A5C08B9FDA0"}],"hostImports":[],"extensions":[]}""")]
