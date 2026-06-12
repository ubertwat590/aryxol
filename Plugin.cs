using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

[BepInPlugin("com.ubertwat590.aryxol", "aryxol", "1.0.0")]
[BepInDependency("com.offiry.qol")]
[BepInDependency("com.nikkorap.blueprinter")]
public class AryxolMod : BaseUnityPlugin
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Regex rMount = new Regex(@"^(?<p>[^\s]+)\s+modifymount\s+(?<f>[^\s]+)\s+(?<v>(""[^""]*""|'[^']*'|[^\s]+))$");
    private static readonly Regex rComp = new Regex(@"^(?<p>(?:""[^""]+""|\S+))\s+addcomponent\s+(?<n>[^\s]+)$");
    private static readonly Regex rWep = new Regex(@"^(?<p>[^\s]+)\s+(?<o>add|remove)weapon(?<i>\d+)\s+(?<n>[^\s]+)$");
    private static readonly Regex rTrs = new Regex(@"^(?<p>(?:""[^""]+""|\S+))\s+transform\s+(?<px>-?\d+\.?\d*)\s+(?<py>-?\d+\.?\d*)\s+(?<pz>-?\d+\.?\d*)\s+(?<rx>-?\d+\.?\d*)\s+(?<ry>-?\d+\.?\d*)\s+(?<rz>-?\d+\.?\d*)\s+(?<sx>-?\d+\.?\d*)\s+(?<sy>-?\d+\.?\d*)\s+(?<sz>-?\d+\.?\d*)$");
    private static readonly Regex rFld = new Regex(@"^(?<p>(?:""[^""]+""|\S+))\s+(?<c>[^\s/]+(?:\/[^\s.]+)*)\s+(?<f>[^\s.]+(?:\[(?<i>\d+)\])?(?:\.[^\s.]+(?:\[\d+\])?)*)\s+(?<o>modify|check|color|modifyobj|modifyvector|modifyaircraft|modifyvehicle|modifyship|modifypart|modifyaudio|modify2|modify3|modify4|modifyactive)\s+(?<v>""[^""]+""|\S+)");
    private static readonly Regex rHP = new Regex(@"^(?<p>(?:""[^""]+""|\S+))\s+hardpointset\s+(?<o>add|name|addhardpoint|modifyhardpoint|precludehardpoint)\s+(?:(?<i>\d+)\s*)?(?:(?<n>(?:""[^""]+""|\S+))\s*)?(?:(?<hi>\d+)\s*)?(?:(?<tp>(?:""[^""]+""|\S+))\s*)?(?:(?<pp>(?:""[^""]+""|\S+))\s*)?$");
    private static readonly Regex rLiv = new Regex(@"^(?<a>(?:""[^""]+""|\S+))\s+modifyliveryname\s+(?<i>\d+)\s+(?<n>(?:""[^""]+""|\S+))$");
    private static readonly Regex rLdt = new Regex(@"^(?<a>(?:""[^""]+""|\S+))\s+modifydefaultloadout\s+(?<i>\d+)\s+(?<m>(?:""[^""]+""|\S+))$");

    private void Awake()
    {
        hideFlags = HideFlags.HideAndDontSave;
        new Harmony("com.ubertwat590.aryxol").PatchAll();
        Init();
    }

    private void Init()
    {
        var asm = typeof(AryxolMod).Assembly;
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("aryxol_commands.txt", StringComparison.OrdinalIgnoreCase));
        if (res == null) return;

        using var s = asm.GetManifestResourceStream(res);
        using var r = new StreamReader(s);

        StartCoroutine(Run(r.ReadToEnd()));
    }

    private IEnumerator Run(string data)
    {
        yield return new WaitForSeconds(10f);
        if (Find("Aryx_LightFighter1") != null) Find("Aryx_LightFighter1").name = "Aryx_LightFighter"; else Logger.LogWarning("Not found");

        foreach (var line in data.Split('\n').Select(l => l.Trim()))
        {
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            Exec(line);
            Logger.LogInfo($"Executed command: {line}");
            yield return null;
        }
    }

    private void Exec(string l)
    {
        if (rFld.IsMatch(l)) DoField(l);
        else if (rHP.IsMatch(l)) DoHP(l);
        else if (rTrs.IsMatch(l)) DoTrs(l);
        else if (rWep.IsMatch(l)) DoWep(l);
        else if (rMount.IsMatch(l)) DoMount(l);
        else if (rComp.IsMatch(l)) DoComp(l);
        else if (rLiv.IsMatch(l)) DoLivery(l);
        else if (rLdt.IsMatch(l)) DoLoadout(l);
    }

    private void DoTrs(string l)
    {
        var m = rTrs.Match(l);
        var target = Find(Fix(m.Groups["p"].Value));
        if (!target) return;

        target.transform.SetPositionAndRotation(
            new Vector3(F(m, "px"), F(m, "py"), F(m, "pz")),
            Quaternion.Euler(F(m, "rx"), F(m, "ry"), F(m, "rz")));
        target.transform.localScale = new Vector3(F(m, "sx"), F(m, "sy"), F(m, "sz"));
    }

    private void DoWep(string l)
    {
        var m = rWep.Match(l);
        var target = Find(m.Groups["p"].Value.Trim());
        if (!target) return;

        var prefab = GetWep(m.Groups["n"].Value.Trim());
        var isAdd = m.Groups["o"].Value == "add";
        int idx = int.Parse(m.Groups["i"].Value) - 1;

        foreach (var mgr in target.GetComponentsInChildren<WeaponManager>(true))
        {
            var set = mgr.hardpointSets[idx];
            if (isAdd)
            {
                if (!set.weaponOptions.Contains(prefab)) set.weaponOptions.Add(prefab);
            }
            else set.weaponOptions.RemoveAll(w => w && w.name.Equals(m.Groups["n"].Value.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    private void DoMount(string l)
    {
        var m = rMount.Match(l);
        var mount = Resources.FindObjectsOfTypeAll<WeaponMount>().FirstOrDefault(x => x.name == m.Groups["p"].Value.Trim());
        if (!mount) return;

        string fName = m.Groups["f"].Value.Trim();
        string val = Fix(m.Groups["v"].Value);

        if (fName == "info")
        {
            mount.info = Resources.FindObjectsOfTypeAll<WeaponInfo>().FirstOrDefault(x => x.name.Equals(val, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var f = mount.GetType().GetField(fName, Flags);
        if (f != null) f.SetValue(mount, Convert.ChangeType(val, f.FieldType, CultureInfo.InvariantCulture));
    }

    private void DoComp(string l)
    {
        var m = rComp.Match(l);
        var target = Find(Fix(m.Groups["p"].Value));
        if (!target) return;

        string name = m.Groups["n"].Value.Trim();
        if (name == "Destroy") { target.SetActive(false); return; }

        var type = GetType(name);
        if (type == null) return;

        var existing = target.GetComponent(type);
        if (existing) Destroy(type == typeof(MountedMissile) ? existing.gameObject : existing);
        else target.AddComponent(type);
    }

    private void DoField(string l)
    {
        var m = rFld.Match(l);
        string path = Fix(m.Groups["p"].Value);
        string op = m.Groups["o"].Value;
        string val = Fix(m.Groups["v"].Value);

        var obj = Find(path);
        if (!obj) { DoWepInfo(path, m.Groups["c"].Value, m.Groups["f"].Value, val); return; }

        var target = Resolve(obj, m.Groups["c"].Value);
        if (target == null) return;

        switch (op)
        {
            case "modify4": DoWepInfo(path, m.Groups["c"].Value, m.Groups["f"].Value, val); break;
            case "modify2": DoMissile(target, m.Groups["f"].Value, val); break;
            case "modify3": DoWepInfoRef(target, m.Groups["f"].Value, val); break;
            case "modifyvector": DoVec(target, m.Groups["f"].Value, val); break;
            case "color": DoCol(target, m.Groups["f"].Value, val); break;
            case "modifyobj": DoObj(target, m.Groups["f"].Value, val); break;
            case "modifyactive": obj.SetActive(bool.Parse(val)); break;
            default: DoStd(target, m.Groups["f"].Value, val); break;
        }
    }

    private void DoHP(string l)
    {
        var m = rHP.Match(l);
        var mgr = Find(m.Groups["p"].Value.Trim())?.GetComponent<WeaponManager>();
        if (!mgr) return;

        string op = m.Groups["o"].Value.ToLower();
        int sIdx = int.TryParse(m.Groups["i"].Value, out var si) ? si : 0;

        if (op == "add")
        {
            var list = mgr.hardpointSets.ToList();
            list.Add(new HardpointSet { name = "NewSet_" + (list.Count + 1), hardpoints = new List<Hardpoint>(), weaponOptions = new List<WeaponMount>() });
            mgr.hardpointSets = list.ToArray();
        }
        else if (op == "name")
        {
            mgr.hardpointSets[sIdx].name = Fix(m.Groups["n"].Value);
            mgr.hardpointSets[sIdx].precludingHardpointSets = new List<byte>();
        }
        else if (op == "addhardpoint")
        {
            mgr.hardpointSets[sIdx].hardpoints.Add(new Hardpoint { transform = new GameObject("HP_" + Guid.NewGuid().ToString().Substring(0, 8)).transform });
        }
        else if (op == "modifyhardpoint")
        {
            int hIdx = int.Parse(m.Groups["hi"].Value);
            var hp = mgr.hardpointSets[sIdx].hardpoints[hIdx];
            var tObj = Find(Fix(m.Groups["tp"].Value));
            if (tObj) hp.transform = tObj.transform;
            var pPath = Fix(m.Groups["pp"].Value);
            if (!string.IsNullOrEmpty(pPath)) hp.part = Resources.FindObjectsOfTypeAll<AeroPart>().FirstOrDefault(x => GetPath(x.transform) == pPath);
        }
        else if (op == "precludehardpoint")
        {
            mgr.hardpointSets[sIdx].precludingHardpointSets.Add((byte)int.Parse(m.Groups["hi"].Value));
        }
    }

    private void DoLivery(string l)
    {
        var m = rLiv.Match(l);
        var p = Resources.FindObjectsOfTypeAll<AircraftParameters>().FirstOrDefault(x => x.name.Equals(Fix(m.Groups["a"].Value), StringComparison.OrdinalIgnoreCase));
        if (!p) return;

        var list = typeof(AircraftParameters).GetField("liveries", Flags).GetValue(p) as IList;
        int idx = int.Parse(m.Groups["i"].Value);
        if (idx < 0 || idx >= list.Count) return;

        var liv = list[idx];
        var f = liv.GetType().GetField("name", Flags);
        if (f != null) f.SetValue(liv, Fix(m.Groups["n"].Value));
    }

    private void DoLoadout(string l)
    {
        var m = rLdt.Match(l);
        var p = Resources.FindObjectsOfTypeAll<AircraftParameters>().FirstOrDefault(x => x.name.Equals(Fix(m.Groups["a"].Value), StringComparison.OrdinalIgnoreCase));
        if (!p) return;

        var lds = typeof(AircraftParameters).GetField("loadouts", Flags).GetValue(p) as IList;
        var weapons = lds[1].GetType().GetField("weapons", Flags).GetValue(lds[1]) as IList;
        int idx = int.Parse(m.Groups["i"].Value);
        var mount = GetWep(Fix(m.Groups["m"].Value));

        while (idx >= weapons.Count) weapons.Add(null);
        weapons[idx] = mount;
    }

    private static GameObject Find(string p)
    {
        if (string.IsNullOrEmpty(p)) return null;

        var parts = p.Split('/');
        var roots = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.transform.parent == null && g.name == parts[0]);

        foreach (var root in roots)
        {
            var cur = root;
            bool ok = true;
            for (int i = 1; i < parts.Length; i++)
            {
                var t = cur.transform.Find(parts[i]);
                if (!t) { ok = false; break; }
                cur = t.gameObject;
            }
            if (ok) return cur;
        }
        return null;
    }

    private object Resolve(GameObject g, string p)
    {
        var parts = p.Split('/', '.');
        var cur = g.GetComponent(parts[0]) ?? g.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (!cur) return null;

        object obj = cur;
        for (int i = 1; i < parts.Length; i++)
        {
            var f = obj.GetType().GetField(parts[i], Flags) ?? obj.GetType().GetFields(Flags).FirstOrDefault(x => x.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (f == null) return null;
            obj = f.GetValue(obj);
        }
        return obj;
    }

    private void DoStd(object t, string n, string v)
    {
        var f = t.GetType().GetField(n, Flags);
        if (f == null) return;
        f.SetValue(t, f.FieldType.IsEnum ? Enum.Parse(f.FieldType, v, true) : Convert.ChangeType(v, f.FieldType, CultureInfo.InvariantCulture));
    }

    private void DoVec(object t, string n, string v)
    {
        var s = v.Split(',');
        var vec = new Vector3(float.Parse(s[0]), float.Parse(s[1]), float.Parse(s[2]));
        t.GetType().GetField(n, Flags)?.SetValue(t, vec);
    }

    private void DoCol(object t, string n, string v)
    {
        var s = v.Split(',');
        var c = new Color(float.Parse(s[0]), float.Parse(s[1]), float.Parse(s[2]), float.Parse(s[3]));
        var f = t.GetType().GetField(n, Flags);
        if (f != null) f.SetValue(t, c);
        else t.GetType().GetProperty(n, Flags)?.SetValue(t, c);
    }

    private void DoObj(object t, string n, string v) => t.GetType().GetField(n, Flags)?.SetValue(t, Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase)));
    private void DoWepInfoRef(object t, string n, string v) => t.GetType().GetField(n, Flags)?.SetValue(t, Resources.FindObjectsOfTypeAll<WeaponInfo>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase)));

    private void DoMissile(object t, string n, string v)
    {
        var f = t.GetType().GetField(n, Flags);
        if (f == null) return;
        object val = null;
        if (f.FieldType == typeof(MissileDefinition)) val = Resources.FindObjectsOfTypeAll<MissileDefinition>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase));
        else if (f.FieldType == typeof(Missile)) val = Resources.FindObjectsOfTypeAll<Missile>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase));
        else if (f.FieldType == typeof(Unit)) val = Resources.FindObjectsOfTypeAll<Unit>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase));
        else if (f.FieldType == typeof(Transform)) val = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(x => x.name.Equals(v, StringComparison.OrdinalIgnoreCase))?.transform;
        f.SetValue(t, val);
    }

    private void DoWepInfo(string p, string cp, string f, string v)
    {
        var types = new[] { typeof(WeaponInfo), typeof(VehicleDefinition), typeof(AircraftDefinition), typeof(MissileDefinition), typeof(BuildingDefinition) };
        object info = new UnityEngine.Object[] {
            Resources.FindObjectsOfTypeAll<MissileDefinition>().FirstOrDefault(x => x.name == p),
            Resources.FindObjectsOfTypeAll<WeaponInfo>().FirstOrDefault(x => x.name == p),
            Resources.FindObjectsOfTypeAll<VehicleDefinition>().FirstOrDefault(x => x.name == p),
            Resources.FindObjectsOfTypeAll<AircraftDefinition>().FirstOrDefault(x => x.name == p)
        }.FirstOrDefault(obj => obj != null);


        if (info == null) return;
        var rf = types.Select(t => t.GetField(cp, Flags)).FirstOrDefault(x => x != null);
        if (rf == null) return;

        var req = rf.GetValue(info);
        var sf = req.GetType().GetField(f);
        if (sf != null)
        {
            sf.SetValue(req, Convert.ChangeType(v, sf.FieldType, CultureInfo.InvariantCulture));
            rf.SetValue(info, req);
        }
    }

    private string Fix(string s) => s.Trim(' ', '"');
    private float F(Match m, string k) => float.Parse(m.Groups[k].Value, CultureInfo.InvariantCulture);
    private string GetPath(Transform t) => t.parent ? GetPath(t.parent) + "/" + t.name : t.name;
    private WeaponMount GetWep(string n) => Resources.Load<WeaponMount>(n) ?? Resources.FindObjectsOfTypeAll<WeaponMount>().FirstOrDefault(x => x.name.Equals(n, StringComparison.OrdinalIgnoreCase));
    private Type GetType(string n) => AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => typeof(Component).IsAssignableFrom(t) && (t.Name == n || t.FullName == n)) ?? Type.GetType($"UnityEngine.{n}, UnityEngine");
}
