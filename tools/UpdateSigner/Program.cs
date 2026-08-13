using System;
using System.IO;
using System.Text;
using System.Xml;
using Chaos.NaCl;

namespace Stride.UpdateSigner;

internal static class Program
{
    private const string EdSignatureAttribute = "http://www.andymatuschak.org/xml-namespaces/sparkle";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "generate":
                    return Generate(args);
                case "sign":
                    return Sign(args);
                case "verify":
                    return Verify(args);
                default:
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Generate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: UpdateSigner generate <private-key-output.key>");
            return 1;
        }

        var seed = Convert.FromBase64String(Ed25519.GeneratePrivateKeySeed());
        var publicKey = Ed25519.PublicKeyFromSeed(seed);

        File.WriteAllBytes(args[1], seed);
        Console.WriteLine($"Private key (32-byte seed) written to {args[1]}");
        Console.WriteLine("Public key (base64):");
        Console.WriteLine(Convert.ToBase64String(publicKey));
        return 0;
    }

    private static int Sign(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: UpdateSigner sign <appcast.xml> <private-key.key>");
            return 1;
        }

        var seed = File.ReadAllBytes(args[2]);
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(args[1]);

        var signed = 0;
        foreach (XmlElement enclosure in doc.SelectNodes("//enclosure"))
        {
            var url = enclosure.GetAttribute("url");
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            var signature = Ed25519.Sign(Encoding.UTF8.GetBytes(url), Ed25519.ExpandedPrivateKeyFromSeed(seed));
            enclosure.SetAttribute("edSignature", EdSignatureAttribute, Convert.ToBase64String(signature));
            signed++;
        }

        if (signed == 0)
        {
            Console.Error.WriteLine("No <enclosure> elements found to sign.");
            return 1;
        }

        doc.Save(args[1]);
        Console.WriteLine($"Signed {signed} enclosure(s) in {args[1]}");
        return 0;
    }

    private static int Verify(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: UpdateSigner verify <appcast.xml> <public-key-base64>");
            return 1;
        }

        var publicKey = Convert.FromBase64String(args[2]);
        var doc = new XmlDocument();
        doc.Load(args[1]);

        var ok = 0;
        var failed = 0;
        foreach (XmlElement enclosure in doc.SelectNodes("//enclosure"))
        {
            var url = enclosure.GetAttribute("url");
            var signature = Convert.FromBase64String(enclosure.GetAttribute("edSignature", EdSignatureAttribute));
            var valid = Ed25519.Verify(signature, Encoding.UTF8.GetBytes(url), publicKey);
            Console.WriteLine($"  {(valid ? "OK" : "FAIL")}  {url}");
            if (valid) ok++; else failed++;
        }

        if (failed > 0)
        {
            Console.Error.WriteLine($"Verification failed for {failed} enclosure(s).");
            return 1;
        }

        Console.WriteLine($"All {ok} signature(s) valid.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Stride appcast signing tool");
        Console.WriteLine("  generate <private-key.key>            create a new Ed25519 keypair");
        Console.WriteLine("  sign <appcast.xml> <private-key.key>  sign enclosure URLs in the appcast");
        Console.WriteLine("  verify <appcast.xml> <pubkey-base64>  verify enclosure signatures");
    }
}