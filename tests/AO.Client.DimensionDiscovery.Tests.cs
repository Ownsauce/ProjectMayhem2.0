using System;
using System.IO;
using AO.Client.Discovery;

internal static class DimensionDiscoveryTests
{
    private static int Main()
    {
        Uri source = DimensionServerUrl.Parse(
            "# launcher endpoint\r\n192.168.8.116:8085/dimensions_v3.txt\r\n");
        Require(source.AbsoluteUri == "http://192.168.8.116:8085/dimensions_v3.txt", "source endpoint");

        const string dimensions =
            "STARTINFO description = AORebirth displayname = AORebirth connect = 2.24.96.30 " +
            "ports = 7500 url = version = 18.8.62 ENDINFO\n" +
            "STARTINFO\n description = AORebirth Local\n displayname = AORebirth Local\n" +
            "connect = 127.0.0.1\n ports = 7500\n url =\n version = 18.8.62\nENDINFO\n" +
            "STARTINFO description = Ithaca displayname = Ithaca connect = 199.241.136.157 " +
            "ports = 7000 url = version = 18.8.62 ENDINFO";

        var parsed = DimensionListParser.Parse(dimensions);
        Require(parsed.Count == 3, "dimension count");
        Require(parsed[0].DisplayName == "AORebirth", "public display name");
        Require(parsed[1].Host == "127.0.0.1", "local host");
        Require(parsed[1].Ports[0] == 7500, "local port");
        Require(parsed[1].Version == "18.8.62", "local version");
        Require(parsed[2].ToServerEndpoint().Address.AbsoluteUri == "tcp://199.241.136.157:7000/", "Ithaca endpoint");

        string fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "project-mayhem-dimension-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            string nestedDirectory = Path.Combine(fixtureRoot, "cd_image", "data", "launcher");
            Directory.CreateDirectory(nestedDirectory);
            string rootConfiguration = Path.Combine(fixtureRoot, "DimensionServer.url");
            string nestedConfiguration = Path.Combine(nestedDirectory, "DimensionServer.url");
            File.WriteAllText(rootConfiguration, "ao-rebirth.com:80/new-dimensions/dimensions_v3.txt");
            File.WriteAllText(nestedConfiguration, "192.168.8.116:8085/dimensions_v3.txt");
            Require(
                DimensionServerLocator.FindConfiguration(fixtureRoot) == nestedConfiguration,
                "combined catalog precedence");
            Require(
                DimensionServerLocator.ReadPreferredEndpoint(fixtureRoot).Authority == "192.168.8.116:8085",
                "combined catalog endpoint");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
                Directory.Delete(fixtureRoot, true);
        }

        Console.WriteLine("PASS AO.Client dimension discovery");
        return 0;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("Failed: " + name);
    }
}
