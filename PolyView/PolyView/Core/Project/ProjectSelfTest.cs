using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;

namespace PolyView.Project;

/// <summary>PolyView.exe --project-selftest out.txt : save a scene (built-in bar + an STL model + a point light) to .pvproj, reopen it in a fresh viewport and compare.</summary>
public static class ProjectSelfTest
{
    public static int Run(string outFile)
    {
        var log = new List<string>();
        int code = 1;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "PolyViewSelfTest");
            Directory.CreateDirectory(dir);
            var stl = Path.Combine(dir, "box.stl");
            MeshBuilder.WriteBinaryStl(MeshBuilder.CreateBar(), stl);

            var a = new ModelViewport();
            a.ShowSample();
            a.AddModelForTest(stl);
            a.AddLight(SceneLightType.Point);
            var ids = a.ObjectIds;
            a.SetTransform(ids[0], new ObjectTransform { Position = new Vector3(1, 2, 3), Rotation = ObjectTransform.FromEuler(new Vector3(10, 20, 30)), Scale = new Vector3(2, 1, 0.5f) });
            a.SetObjectVisible(ids[1], false);
            a.Shading = ViewportShading.Rendered;
            a.Select(ids[2]);
            var before = a.CaptureProject();
            var file = Path.Combine(dir, "selftest" + ProjectPackage.Extension);
            ProjectPackage.Write(file, before, null, ProjectSaveOptions.Default);
            ProjectPackage.Write(file, before, null, ProjectSaveOptions.Default); // second save -> .pvproj1 backup
            log.Add($"saved {file} ({new FileInfo(file).Length} bytes), backup exists: {File.Exists(Path.ChangeExtension(file, ProjectPackage.BackupExtension))}");

            var (doc, assets, missing) = ProjectPackage.Read(file);
            var imports = ModelViewport.ImportProjectAssets(doc, assets);
            var b = new ModelViewport();
            var (map, failed) = b.RestoreProject(doc, assets, imports);
            var after = b.CaptureProject();
            log.Add($"objects {before.Objects.Count} -> {after.Objects.Count}, missing {missing.Count}, failed {failed.Count}");

            string want = Normalize(before), got = Normalize(after);
            if (want == got && missing.Count == 0 && failed.Count == 0)
            {
                log.Add("PASS");
                code = 0;
            }
            else
            {
                log.Add("FAIL\n--- before ---\n" + want + "\n--- after ---\n" + got);
            }
        }
        catch (Exception ex)
        {
            log.Add("FAIL " + ex);
        }

        File.WriteAllLines(outFile, log);
        return code;
    }

    /// <summary>JSON with object ids renumbered by order, and volatile fields (time, cache paths) removed.</summary>
    static string Normalize(ProjectDocument doc)
    {
        var node = JsonNode.Parse(ProjectSerializer.Serialize(doc))!;
        node["savedAt"] = null;
        foreach (var asset in node["assets"]!.AsArray())
        {
            asset!["originalPath"] = null;
        }

        var idMap = new Dictionary<int, int>();
        var objects = node["objects"]!.AsArray();
        for (int i = 0; i < objects.Count; i++)
        {
            idMap[(int)objects[i]!["id"]!] = i;
            objects[i]!["id"] = i;
        }

        int sel = (int)node["selection"]!["objectId"]!;
        node["selection"]!["objectId"] = idMap.TryGetValue(sel, out int s) ? s : -1;
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
