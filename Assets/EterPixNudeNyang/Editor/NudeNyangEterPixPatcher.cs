using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EterPixNudeNyang
{
    // NudeNyang本体(gitignore下の第三者製アセット)へ、EterPix-appを載せるための変更を当てる.
    // 冪等: 変更後の文字列が既にあれば何もしない. 変更前の文字列がちょうど期待回数なければ停止して報告する
    // (NudeNyangが更新された可能性). 初回は <名前>.eterpix-orig として元ファイルを隣に残す.
    public static class NudeNyangEterPixPatcher
    {
        // 製品の想定の置き場所は Assets/NudeNyang. 旧い配置(同期しないフォルダ内)も見つかれば使う
        public static string Root
        {
            get
            {
                return Directory.Exists("Assets/NudeNyang/Scripts") ? "Assets/NudeNyang" : "Assets/アセット(同期しない)/NudeNyang";
            }
        }
        public const string ExpectedVersion = "1.0.2";

        sealed class Patch
        {
            public string Id, File, Anchor, Replacement; public int Expected;
            public Patch(string id, string file, string anchor, string replacement, int expected = 1)
            { Id = id; File = file; Anchor = anchor; Replacement = replacement; Expected = expected; }
        }

        const string Shell = "Scripts/NudeNyangTabletShell.cs";
        const string Shared = "Scripts/NudeNyangSharedControls.cs";
        const string ShellBuilder = "Editor/NudeNyangTabletShellBuilder.cs";
        const string ThemeBuilder = "Editor/NudeNyangTabletThemeBuilder.cs";

        static string Before(string anchor, string add) { return add + anchor; }
        static string After(string anchor, string add) { return anchor + add; }

        static Patch[] Patches()
        {
            const string travelOpen = "public void OpenTravel(){if(Route(\"OpenTravel\"))LaunchFrom(11,homeIcons[10]);}";
            const string travelName = "if(id==11)return pad.T(\"트래블 월드\");";
            const string travelField = "public NudeNyangTravel travel;";
            const string presentTravel = "if(travel!=null)travel.Present();";
            const string names = "i==9?\"화면 공유\":\"트래블 월드\",action=i==7?\"OpenWeather\":i==8?\"OpenBooth\":i==9?\"OpenScreenShare\":\"OpenTravel\"";
            const string namesNew = "i==9?\"화면 공유\":i==11?\"EterPix\":\"트래블 월드\",action=i==7?\"OpenWeather\":i==8?\"OpenBooth\":i==9?\"OpenScreenShare\":i==11?\"OpenEterPix\":\"OpenTravel\"";
            const string sharedHome = "OpenScreenShare|OpenTravel|Recent0";
            const string sharedKeyboard = "else if(target==5)methods=\"|Open|OpenX|OpenBooth|OpenTravel|\";";
            const string themeAnchor = "if(pad.album!=null&&pad.album.urlActions!=null)excluded.Add(pad.album.urlActions.GetComponent<Image>());";
            return new[]
            {
                new Patch("P1", Shell, "public const int AppCount=11;", "public const int AppCount=12;"),
                new Patch("P2", Shell, travelField, After(travelField, "public NudeNyangEterPix eterpix;")),
                new Patch("P3", Shell, travelName, Before(travelName, "if(id==12)return \"EterPix\";")),
                new Patch("P4", Shell, travelOpen, After(travelOpen, "public void OpenEterPix(){if(Route(\"OpenEterPix\"))LaunchFrom(12,homeIcons[11]);}")),
                new Patch("P6", Shell, presentTravel, After(presentTravel, "if(eterpix!=null)eterpix.Present();")),
                new Patch("P7", Shared, sharedHome, "OpenScreenShare|OpenTravel|OpenEterPix|Recent0"),
                new Patch("P8", Shared, sharedKeyboard, "else if(target==5)methods=\"|Open|OpenX|OpenBooth|OpenTravel|OpenEterPix|\";"),
                new Patch("P9", ShellBuilder, names, namesNew, 2),
                // NudeNyangのテーマ(色の役割の自動推定)が、EterPix-appの色を上書きしないよう走査から外す.
                // EterPix-app の黒/白は独立(合意書にNudeNyangテーマ連動の記載なし)
                new Patch("P11", ThemeBuilder, themeAnchor, After(themeAnchor,
                    "if(pad.shell.eterpix!=null&&pad.shell.eterpix.panel!=null)foreach(var g in pad.shell.eterpix.panel.GetComponentsInChildren<Graphic>(true))excluded.Add(g);")),
            };
        }

        static string Path(Patch p) { return Root + "/" + p.File; }

        // 0=未適用 1=適用済 2=アンカー不一致(NudeNyangが更新された可能性) 3=ファイル無し
        static int State(Patch p, string text)
        {
            if (text == null) return 3;
            if (text.Contains(p.Replacement)) return 1;
            return Count(text, p.Anchor) == p.Expected ? 0 : 2;
        }

        static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        static string Read(string path, out bool bom)
        {
            bom = false;
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }

        [MenuItem("NudeNyang/EterPix-app/パッチを検査")]
        public static void Inspect()
        {
            CheckVersion();
            foreach (var p in Patches())
            {
                bool bom; string text = Read(Path(p), out bom);
                string[] label = { "未適用", "適用済", "アンカー不一致", "ファイル無し" };
                Debug.Log("[EtpTrace] patch " + p.Id + " " + label[State(p, text)] + " (" + p.File + ")");
            }
        }

        [MenuItem("NudeNyang/EterPix-app/パッチを適用")]
        public static void Apply()
        {
            CheckVersion();
            var patches = Patches();
            // 先に全部検査し、1つでも不一致なら何も書かない
            foreach (var p in patches)
            {
                bool bom; int s = State(p, Read(Path(p), out bom));
                if (s >= 2)
                {
                    Debug.LogError("[EtpTrace] patch " + p.Id + " を当てられません (" + (s == 2 ? "アンカー不一致" : "ファイル無し") + "): " + p.File + " 何も書き換えていません");
                    return;
                }
            }
            int applied = 0;
            foreach (var group in patches.GroupBy(p => p.File))
            {
                string path = Root + "/" + group.Key;
                bool bom; string text = Read(path, out bom);
                bool changed = false;
                foreach (var p in group)
                {
                    if (State(p, text) != 0) continue;
                    text = text.Replace(p.Anchor, p.Replacement); changed = true; applied++;
                }
                if (!changed) continue;
                if (!File.Exists(path + ".eterpix-orig")) File.Copy(path, path + ".eterpix-orig");
                File.WriteAllBytes(path, new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text)).ToArray());
            }
            Debug.Log("[EtpTrace] patch 適用 " + applied + " 件");
            AssetDatabase.Refresh();
        }

        static void CheckVersion()
        {
            string path = Root + "/InstallManager/Editor/Updates/installed-release.json";
            string json = File.Exists(path) ? File.ReadAllText(path) : "";
            if (!json.Contains("\"version\": \"" + ExpectedVersion + "\""))
                Debug.LogWarning("[EtpTrace] NudeNyangのバージョンが " + ExpectedVersion + " ではありません. パッチのアンカーを見直してください");
        }

        public static bool AllApplied()
        {
            return Patches().All(p => { bool bom; return State(p, Read(Path(p), out bom)) == 1; });
        }
    }
}
