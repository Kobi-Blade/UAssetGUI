using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Windows.Forms;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetGUI
{
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        internal static string ExtractCompressedResource(string resourceName, string outPath, Assembly targetAsm = null)
        {
            using (var stream = (targetAsm ?? typeof(Program).Assembly).GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;

                byte[] newStreamHash;
                using (SHA256 hash = SHA256.Create())
                {
                    newStreamHash = hash.ComputeHash(stream);
                    stream.Seek(0, SeekOrigin.Begin);
                }

                string streamHashPath = Path.ChangeExtension(outPath, Path.GetExtension(outPath) + ".sha256");
                byte[] currentStreamHash = File.Exists(streamHashPath)
                    ? File.ReadAllBytes(streamHashPath)
                    : Array.Empty<byte>();

                if (currentStreamHash.Length > 0 && currentStreamHash.SequenceEqual(newStreamHash) && File.Exists(outPath))
                {
                    return outPath;
                }

                try
                {
                    using (FileStream newFileStream = File.Open(outPath, FileMode.Create, FileAccess.Write))
                    using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
                    {
                        gzipStream.CopyTo(newFileStream);
                    }

                    File.WriteAllBytes(streamHashPath, newStreamHash);
                    return outPath;
                }
                catch
                {
                    return File.Exists(outPath) ? outPath : null;
                }
            }
        }

        private static List<Type> strongRefs = new List<Type>();
        public static List<string> args;

        [STAThread]
        public static void Main()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetDefaultFont(new Font(new FontFamily("Microsoft Sans Serif"), 8.25f));

                args = Environment.GetCommandLineArgs().ToList();

                if (args.Count >= 2)
                {
                    Usmap selectedMappings = null;

                    switch (args[1].ToLowerInvariant())
                    {
                        case "tojson":
                            UAGConfig.LoadMappings();

                            if (args.Count < 5) break;
                            if (args.Count >= 6) UAGConfig.TryGetMappings(args[5], out selectedMappings);

                            EngineVersion selectedVer = EngineVersion.UNKNOWN;
                            if (int.TryParse(args[4], out int selectedVerRaw)) selectedVer = EngineVersion.VER_UE4_0 + selectedVerRaw;
                            else Enum.TryParse(args[4], out selectedVer);

                            string jsonSerializedAsset = new UAsset(args[2], selectedVer, selectedMappings).SerializeJson(Newtonsoft.Json.Formatting.Indented);
                            File.WriteAllText(args[3], jsonSerializedAsset);
                            return;

                        case "fromjson":
                            UAGConfig.LoadMappings();

                            if (args.Count < 4) break;
                            if (args.Count >= 5) UAGConfig.TryGetMappings(args[4], out selectedMappings);

                            UAsset jsonDeserializedAsset;
                            using (var sr = new FileStream(args[2], FileMode.Open))
                                jsonDeserializedAsset = UAsset.DeserializeJson(sr);

                            if (jsonDeserializedAsset != null)
                            {
                                jsonDeserializedAsset.Mappings = selectedMappings;
                                jsonDeserializedAsset.FilePath = args[2];
                                jsonDeserializedAsset.Write(args[3]);
                            }
                            return;
                    }
                }

                try
                {
                    AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
                    {
                        string libsPath = Path.Combine(UAGConfig.ConfigFolder, "Libraries");
                        Directory.CreateDirectory(UAGConfig.ConfigFolder);
                        Directory.CreateDirectory(libsPath);

                        string outPath = ExtractCompressedResource("UAssetGUI." + assemblyName.Name + ".dll.gz",Path.Combine(libsPath, assemblyName.Name + ".dll"));
                        return outPath == null ? null : Assembly.LoadFrom(outPath);
                    };

                    strongRefs.Add(typeof(System.Collections.Immutable.ImmutableArray));
                    strongRefs.Add(typeof(System.Reflection.Metadata.MetadataReader));
                }
                catch (Exception ex)
                {
                    Clipboard.SetText(ex.ToString());
                }

                Form1 f1 = new Form1
                {
                    Size = new Size(1000, 700)
                };

                Application.Run(f1);
            }
            catch (Exception ex)
            {
                Clipboard.SetText(ex.ToString());
            }
        }
    }
}