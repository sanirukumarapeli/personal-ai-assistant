using PersonalAi.Core;

namespace PersonalAi.Tests;

public class EnvFileLoaderTests
{
    [Fact]
    public void LoadNearest_FindsEnvInAncestor()
    {
        var temp = Directory.CreateTempSubdirectory("pa-env-test");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, ".env"), "PA_TEST_KEY=hello\n");
            var nested = Directory.CreateDirectory(Path.Combine(temp.FullName, "a", "b"));
            var found = EnvFileLoader.LoadNearest(nested.FullName);
            Assert.NotNull(found);
            Assert.Equal("hello", Environment.GetEnvironmentVariable("PA_TEST_KEY"));
        }
        finally
        {
            try { temp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
