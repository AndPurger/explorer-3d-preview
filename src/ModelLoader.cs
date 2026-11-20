// Leitura de arquivos 3MF (incluindo projetos do Bambu Studio com várias plates), STL e OBJ.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Bambu3DPreview
{
    public class MeshData
    {
        public List<float> V = new List<float>();   // x,y,z,x,y,z...
        public List<int> T = new List<int>();       // v1,v2,v3,...
        public int TriangleCount { get { return T.Count / 3; } }
    }

    public class Placement
    {
        public MeshData Mesh;
        public Matrix3D Matrix;
        public Color Color;
    }

    public class PlateObject
    {
        public string Name;
        public List<Placement> Parts = new List<Placement>();
    }

    public class PlateData
    {
        public int Number;
        public string Name;
        public byte[] Thumbnail;
        public List<PlateObject> Objects = new List<PlateObject>();
        public Rect3D Bed = Rect3D.Empty; // mesa da impressora (quando conhecida)
    }

    public class SceneData
    {
        public string Format;
        public List<PlateData> Plates = new List<PlateData>();
    }

    public static class ModelLoader
    {
        public static readonly Color DefaultColor = Color.FromRgb(0x5E, 0xC2, 0x9A);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static SceneData Load(string path, byte[] data, Func<bool> cancelled)
        {
            if (data == null) data = File.ReadAllBytes(path);
            string ext = path == null ? "" : Path.GetExtension(path).ToLowerInvariant();
            if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K') return Load3mf(data, cancelled);
            if (ext == ".obj") return LoadObj(data);
            if (ext == ".stl" || LooksLikeStl(data)) return LoadStl(data);
            return LoadObj(data);
        }

        static SceneData Single(string format, string name, MeshData mesh)
        {
            var scene = new SceneData { Format = format };
            var plate = new PlateData { Number = 1, Name = "" };
            var obj = new PlateObject { Name = name };
            obj.Parts.Add(new Placement { Mesh = mesh, Matrix = Matrix3D.Identity, Color = DefaultColor });
            plate.Objects.Add(obj);
            scene.Plates.Add(plate);
            return scene;
        }

        // ---------------- STL ----------------
        static bool LooksLikeStl(byte[] d)
        {
            if (d.Length >= 84)
            {
                uint n = BitConverter.ToUInt32(d, 80);
                if (84 + (long)n * 50 == d.Length) return true;
            }
            return d.Length > 5 && Encoding.ASCII.GetString(d, 0, 5).Equals("solid", StringComparison.OrdinalIgnoreCase);
        }

        static SceneData LoadStl(byte[] d)
        {
            var mesh = new MeshData();
            bool binary = false;
            if (d.Length >= 84)
            {
                uint n = BitConverter.ToUInt32(d, 80);
                if (84 + (long)n * 50 == d.Length) binary = true;
            }
            if (binary)
            {
                uint n = BitConverter.ToUInt32(d, 80);
                mesh.V.Capacity = (int)n * 9; mesh.T.Capacity = (int)n * 3;
                int o = 84;
                for (uint i = 0; i < n; i++)
                {
                    int b = o + 12;
                    for (int k = 0; k < 9; k++) mesh.V.Add(BitConverter.ToSingle(d, b + k * 4));
                    int vi = (int)i * 3;
                    mesh.T.Add(vi); mesh.T.Add(vi + 1); mesh.T.Add(vi + 2);
                    o += 50;
                }
            }
            else
            {
                string text = Encoding.ASCII.GetString(d);
                int count = 0;
                foreach (var line in text.Split('\n'))
                {
                    var s = line.Trim();
                    if (!s.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                    var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    mesh.V.Add(ParseF(p[1])); mesh.V.Add(ParseF(p[2])); mesh.V.Add(ParseF(p[3]));
                    count++;
                    if (count % 3 == 0) { mesh.T.Add(count - 3); mesh.T.Add(count - 2); mesh.T.Add(count - 1); }
                }
            }
            return Single("STL", "Malha STL", mesh);
        }

        // ---------------- OBJ ----------------
        static SceneData LoadObj(byte[] d)
        {
            var mesh = new MeshData();
            string text = Encoding.UTF8.GetString(d);
            int vcount = 0;
            var idx = new List<int>();
            foreach (var raw in text.Split('\n'))
            {
                var s = raw.Trim();
                if (s.StartsWith("v "))
                {
                    var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    mesh.V.Add(ParseF(p[1])); mesh.V.Add(ParseF(p[2])); mesh.V.Add(ParseF(p[3]));
                    vcount++;
                }
                else if (s.StartsWith("f "))
                {
                    var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    idx.Clear();
                    for (int i = 1; i < p.Length; i++)
                    {
                        string t = p[i];
                        int slash = t.IndexOf('/');
                        if (slash >= 0) t = t.Substring(0, slash);
                        int v;
                        if (!int.TryParse(t, NumberStyles.Integer, Inv, out v)) continue;
                        idx.Add(v < 0 ? vcount + v : v - 1);
                    }
                    for (int i = 1; i + 1 < idx.Count; i++) { mesh.T.Add(idx[0]); mesh.T.Add(idx[i]); mesh.T.Add(idx[i + 1]); }
                }
            }
            // OBJ costuma ser Y-up: gira para Z-up
            var scene = Single("OBJ", "Malha OBJ", mesh);
            var m = new Matrix3D(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1);
            scene.Plates[0].Objects[0].Parts[0].Matrix = m;
            return scene;
        }

        static float ParseF(string s)
        {
            float f;
            float.TryParse(s, NumberStyles.Float, Inv, out f);
            return f;
        }

        // ---------------- 3MF ----------------
        class Comp { public string Path; public int ObjId; public Matrix3D M; }
        class ObjRes { public int Id; public string Name; public MeshData Mesh; public List<Comp> Comps = new List<Comp>(); }
        class Item { public int ObjId; public Matrix3D M; }
        class ModelFile { public Dictionary<int, ObjRes> Objects = new Dictionary<int, ObjRes>(); public List<Item> Items = new List<Item>(); }
        class BambuPlate { public int Id; public string Name; public string Thumb; public List<int[]> Instances = new List<int[]>(); }

        class Ctx
        {
            public ZipArchive Zip;
            public Dictionary<string, ZipArchiveEntry> Entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, ModelFile> Files = new Dictionary<string, ModelFile>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<int, string> PartSubtype = new Dictionary<int, string>();
            public Dictionary<int, int> PartExtruder = new Dictionary<int, int>();
            public Dictionary<int, int> ObjExtruder = new Dictionary<int, int>();
            public Dictionary<int, string> ObjName = new Dictionary<int, string>();
            public List<Color> Filaments = new List<Color>();
            public Func<bool> Cancelled;
        }

        static string Norm(string p) { return p.Replace('\\', '/').TrimStart('/'); }

        static ZipArchiveEntry Entry(Ctx c, string p)
        {
            ZipArchiveEntry e;
            return c.Entries.TryGetValue(Norm(p), out e) ? e : null;
        }

        static string ReadText(Ctx c, string p)
        {
            var e = Entry(c, p);
            if (e == null) return null;
            using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd();
        }

        static byte[] ReadBytes(Ctx c, string p)
        {
            var e = Entry(c, p);
            if (e == null) return null;
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static SceneData Load3mf(byte[] data, Func<bool> cancelled)
        {
            var scene = new SceneData { Format = "3MF" };
            using (var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read))
            {
                var c = new Ctx { Zip = zip, Cancelled = cancelled };
                foreach (var e in zip.Entries) c.Entries[Norm(e.FullName)] = e;

                string rootPath = FindRootModel(c);
                var root = GetFile(c, rootPath);
                ReadBambuConfig(c);
                var bambuPlates = ReadBambuPlates(c);
                ReadFilaments(c);
                Rect3D bedSize = ReadBedSize(c);

                // instâncias: itens do build agrupados por objeto, na ordem em que aparecem
                var instances = new Dictionary<int, List<Item>>();
                foreach (var it in root.Items)
                {
                    List<Item> l;
                    if (!instances.TryGetValue(it.ObjId, out l)) { l = new List<Item>(); instances[it.ObjId] = l; }
                    l.Add(it);
                }

                if (bambuPlates.Count > 0)
                {
                    int cols = (int)Math.Ceiling(Math.Sqrt(bambuPlates.Count));
                    for (int pi = 0; pi < bambuPlates.Count; pi++)
                    {
                        var bp = bambuPlates[pi];
                        var plate = new PlateData { Number = bp.Id > 0 ? bp.Id : pi + 1, Name = bp.Name };
                        plate.Thumbnail = ReadBytes(c, bp.Thumb ?? ("Metadata/plate_" + plate.Number + ".png"));
                        foreach (var inst in bp.Instances)
                        {
                            List<Item> l;
                            if (!instances.TryGetValue(inst[0], out l) || l.Count == 0) continue;
                            var item = l[Math.Min(Math.Max(inst[1], 0), l.Count - 1)];
                            plate.Objects.Add(MakeObject(c, rootPath, root, item));
                        }
                        if (!bedSize.IsEmpty)
                        {
                            int idx = plate.Number - 1;
                            double stride = bedSize.SizeX * 1.2;
                            double ox = (idx % cols) * stride, oy = -(idx / cols) * stride;
                            plate.Bed = new Rect3D(bedSize.X + ox, bedSize.Y + oy, 0, bedSize.SizeX, bedSize.SizeY, 0);
                        }
                        scene.Plates.Add(plate);
                    }
                }
                else
                {
                    var plate = new PlateData { Number = 1, Name = "" };
                    plate.Thumbnail = ReadBytes(c, "Metadata/plate_1.png") ?? ReadBytes(c, "Metadata/thumbnail.png") ?? FindAnyThumbnail(c);
                    foreach (var it in root.Items) plate.Objects.Add(MakeObject(c, rootPath, root, it));
                    scene.Plates.Add(plate);
                }
            }
            return scene;
        }

        static byte[] FindAnyThumbnail(Ctx c)
        {
            foreach (var kv in c.Entries)
                if (kv.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && kv.Key.IndexOf("thumbnail", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReadBytes(c, kv.Key);
            return null;
        }

        static string FindRootModel(Ctx c)
        {
            string rels = ReadText(c, "_rels/.rels");
            if (rels != null)
            {
                var m = Regex.Match(rels, "<Relationship[^>]*Target=\"([^\"]+)\"[^>]*Type=\"[^\"]*3dmodel\"|<Relationship[^>]*Type=\"[^\"]*3dmodel\"[^>]*Target=\"([^\"]+)\"");
                if (m.Success)
                {
                    string t = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (Entry(c, t) != null) return Norm(t);
                }
            }
            foreach (var k in c.Entries.Keys)
                if (k.EndsWith(".model", StringComparison.OrdinalIgnoreCase) && k.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) && k.IndexOf('/', 3) < 0)
                    return k;
            return "3D/3dmodel.model";
        }

        static PlateObject MakeObject(Ctx c, string file, ModelFile mf, Item item)
        {
            ObjRes res;
            mf.Objects.TryGetValue(item.ObjId, out res);
            string name;
            if (!c.ObjName.TryGetValue(item.ObjId, out name) || string.IsNullOrEmpty(name))
                name = res != null && !string.IsNullOrEmpty(res.Name) ? res.Name : "Objeto " + item.ObjId;
            var po = new PlateObject { Name = name };
            int ext;
            if (!c.ObjExtruder.TryGetValue(item.ObjId, out ext)) ext = 1;
            if (res != null) Flatten(c, file, res, item.M, ext, po.Parts, 0);
            return po;
        }

        static void Flatten(Ctx c, string file, ObjRes res, Matrix3D m, int extruder, List<Placement> outList, int depth)
        {
            if (depth > 16) return;
            if (res.Mesh != null && res.Mesh.T.Count > 0)
                outList.Add(new Placement { Mesh = res.Mesh, Matrix = m, Color = FilamentColor(c, extruder) });
            foreach (var comp in res.Comps)
            {
                string sub;
                if (c.PartSubtype.TryGetValue(comp.ObjId, out sub) && sub != "normal_part") continue;
                string target = comp.Path != null ? Norm(comp.Path) : file;
                var f = GetFile(c, target);
                if (f == null) continue;
                ObjRes child;
                if (!f.Objects.TryGetValue(comp.ObjId, out child)) continue;
                int ext;
                if (!c.PartExtruder.TryGetValue(comp.ObjId, out ext)) ext = extruder;
                Flatten(c, target, child, comp.M * m, ext, outList, depth + 1);
            }
        }

        static Color FilamentColor(Ctx c, int extruder)
        {
            if (c.Filaments.Count == 0) return DefaultColor;
            int i = extruder - 1;
            if (i < 0 || i >= c.Filaments.Count) i = 0;
            return c.Filaments[i];
        }

        static ModelFile GetFile(Ctx c, string path)
        {
            path = Norm(path);
            ModelFile mf;
            if (c.Files.TryGetValue(path, out mf)) return mf;
            var e = Entry(c, path);
            if (e == null) { c.Files[path] = null; return null; }
            mf = ParseModel(c, e);
            c.Files[path] = mf;
            return mf;
        }

        static Matrix3D ParseTransform(string s)
        {
            if (string.IsNullOrEmpty(s)) return Matrix3D.Identity;
            var p = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 12) return Matrix3D.Identity;
            var v = new double[12];
            for (int i = 0; i < 12; i++) double.TryParse(p[i], NumberStyles.Float, Inv, out v[i]);
            return new Matrix3D(v[0], v[1], v[2], 0, v[3], v[4], v[5], 0, v[6], v[7], v[8], 0, v[9], v[10], v[11], 1);
        }

        static string Attr(XmlReader r, string local)
        {
            if (r.MoveToFirstAttribute())
            {
                do
                {
                    if (r.LocalName == local) { string v = r.Value; r.MoveToElement(); return v; }
                } while (r.MoveToNextAttribute());
                r.MoveToElement();
            }
            return null;
        }

        static float FastFloat(string s)
        {
            // parser rápido para números simples; cai no padrão quando há expoente
            int i = 0, n = s.Length;
            bool neg = false;
            if (n == 0) return 0;
            if (s[0] == '-') { neg = true; i++; } else if (s[0] == '+') i++;
            long ip = 0; double frac = 0, scale = 1;
            for (; i < n; i++)
            {
                char ch = s[i];
                if (ch >= '0' && ch <= '9') ip = ip * 10 + (ch - '0');
                else break;
            }
            if (i < n && s[i] == '.')
            {
                i++;
                long fp = 0; int digits = 0;
                for (; i < n; i++)
                {
                    char ch = s[i];
                    if (ch >= '0' && ch <= '9') { if (digits < 15) { fp = fp * 10 + (ch - '0'); scale *= 10; digits++; } }
                    else break;
                }
                frac = fp / scale;
            }
            if (i < n) return ParseF(s);
            double v = ip + frac;
            return (float)(neg ? -v : v);
        }

        static int FastInt(string s)
        {
            int v = 0;
            for (int i = 0; i < s.Length; i++) { char ch = s[i]; if (ch >= '0' && ch <= '9') v = v * 10 + (ch - '0'); }
            return v;
        }

        static ModelFile ParseModel(Ctx c, ZipArchiveEntry e)
        {
            var mf = new ModelFile();
            var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore };
            using (var s = e.Open())
            using (var r = XmlReader.Create(s, settings))
            {
                ObjRes cur = null;
                int counter = 0;
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    switch (r.LocalName)
                    {
                        case "vertex":
                            if (cur != null && cur.Mesh != null)
                            {
                                cur.Mesh.V.Add(FastFloat(Attr(r, "x") ?? "0"));
                                cur.Mesh.V.Add(FastFloat(Attr(r, "y") ?? "0"));
                                cur.Mesh.V.Add(FastFloat(Attr(r, "z") ?? "0"));
                            }
                            break;
                        case "triangle":
                            if (cur != null && cur.Mesh != null)
                            {
                                cur.Mesh.T.Add(FastInt(Attr(r, "v1") ?? "0"));
                                cur.Mesh.T.Add(FastInt(Attr(r, "v2") ?? "0"));
                                cur.Mesh.T.Add(FastInt(Attr(r, "v3") ?? "0"));
                                if ((++counter & 0xFFFF) == 0 && c.Cancelled != null && c.Cancelled()) throw new OperationCanceledException();
                            }
                            break;
                        case "object":
                            if (r.NamespaceURI.Contains("3dmanufacturing"))
                            {
                                cur = new ObjRes { Name = Attr(r, "name") };
                                int.TryParse(Attr(r, "id"), out cur.Id);
                                mf.Objects[cur.Id] = cur;
                            }
                            break;
                        case "mesh":
                            if (cur != null && cur.Mesh == null) cur.Mesh = new MeshData();
                            break;
                        case "component":
                            if (cur != null)
                            {
                                var cp = new Comp { Path = Attr(r, "path"), M = ParseTransform(Attr(r, "transform")) };
                                int.TryParse(Attr(r, "objectid"), out cp.ObjId);
                                cur.Comps.Add(cp);
                            }
                            break;
                        case "item":
                            {
                                var it = new Item { M = ParseTransform(Attr(r, "transform")) };
                                int.TryParse(Attr(r, "objectid"), out it.ObjId);
                                mf.Items.Add(it);
                            }
                            break;
                    }
                }
            }
            return mf;
        }

        static void ReadBambuConfig(Ctx c)
        {
            string xml = ReadText(c, "Metadata/model_settings.config");
            if (xml == null) return;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                foreach (XmlElement obj in doc.SelectNodes("/config/object"))
                {
                    int id; int.TryParse(obj.GetAttribute("id"), out id);
                    foreach (XmlElement md in obj.SelectNodes("metadata"))
                    {
                        if (md.GetAttribute("key") == "name") c.ObjName[id] = md.GetAttribute("value");
                        if (md.GetAttribute("key") == "extruder") { int x; if (int.TryParse(md.GetAttribute("value"), out x)) c.ObjExtruder[id] = x; }
                    }
                    foreach (XmlElement part in obj.SelectNodes("part"))
                    {
                        int pid; int.TryParse(part.GetAttribute("id"), out pid);
                        string sub = part.GetAttribute("subtype");
                        c.PartSubtype[pid] = string.IsNullOrEmpty(sub) ? "normal_part" : sub;
                        foreach (XmlElement md in part.SelectNodes("metadata"))
                            if (md.GetAttribute("key") == "extruder") { int x; if (int.TryParse(md.GetAttribute("value"), out x)) c.PartExtruder[pid] = x; }
                    }
                }
            }
            catch { }
        }

        static List<BambuPlate> ReadBambuPlates(Ctx c)
        {
            var list = new List<BambuPlate>();
            string xml = ReadText(c, "Metadata/model_settings.config");
            if (xml == null) return list;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                foreach (XmlElement pl in doc.SelectNodes("/config/plate"))
                {
                    var bp = new BambuPlate();
                    foreach (XmlElement md in pl.SelectNodes("metadata"))
                    {
                        string k = md.GetAttribute("key"), v = md.GetAttribute("value");
                        if (k == "plater_id") int.TryParse(v, out bp.Id);
                        else if (k == "plater_name") bp.Name = v;
                        else if (k == "thumbnail_file") bp.Thumb = v;
                    }
                    foreach (XmlElement mi in pl.SelectNodes("model_instance"))
                    {
                        int oid = -1, iid = 0;
                        foreach (XmlElement md in mi.SelectNodes("metadata"))
                        {
                            if (md.GetAttribute("key") == "object_id") int.TryParse(md.GetAttribute("value"), out oid);
                            if (md.GetAttribute("key") == "instance_id") int.TryParse(md.GetAttribute("value"), out iid);
                        }
                        if (oid >= 0) bp.Instances.Add(new[] { oid, iid });
                    }
                    list.Add(bp);
                }
            }
            catch { }
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        static string ProjectSettings(Ctx c)
        {
            return ReadText(c, "Metadata/project_settings.config");
        }

        static void ReadFilaments(Ctx c)
        {
            string s = ProjectSettings(c);
            if (s == null) return;
            var m = Regex.Match(s, "\"filament_colou?r\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return;
            foreach (Match h in Regex.Matches(m.Groups[1].Value, "#([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?"))
            {
                int rgb = int.Parse(h.Groups[1].Value, NumberStyles.HexNumber);
                c.Filaments.Add(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            }
        }

        static Rect3D ReadBedSize(Ctx c)
        {
            string s = ProjectSettings(c);
            if (s == null) return Rect3D.Empty;
            var m = Regex.Match(s, "\"printable_area\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return Rect3D.Empty;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Match p in Regex.Matches(m.Groups[1].Value, "(-?[0-9.]+)x(-?[0-9.]+)"))
            {
                double x = double.Parse(p.Groups[1].Value, Inv), y = double.Parse(p.Groups[2].Value, Inv);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            if (maxX <= minX || maxY <= minY) return Rect3D.Empty;
            return new Rect3D(minX, minY, 0, maxX - minX, maxY - minY, 0);
        }
    }
}
