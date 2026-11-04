using System.IO;
using UnityEditor;
using UnityEngine;

namespace EterPix.Editor
{
    // 配布版パッケージ内の NudeNyangAddon~ を Assets/EterPixNudeNyang へコピーする.
    // (パッケージ内のスクリプトは Assets/NudeNyang を参照できないため Assets 側に置く)
    public static class EterPixNudeNyangAddonInstaller
    {
        const string Dest = "Assets/EterPixNudeNyang";

        [MenuItem("NudeNyang/EterPix-app/アドオンを Assets へ導入")]
        public static void Install()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(EterPixNudeNyangAddonInstaller).Assembly);
            string src = info == null ? null : Path.Combine(info.resolvedPath, "NudeNyangAddon~");
            if (src == null || !Directory.Exists(src))
            {
                EditorUtility.DisplayDialog("EterPix-app", "パッケージ内に NudeNyangAddon~ がありません(配布版 2.6.1 以上で使えます).", "OK");
                return;
            }
            int n = Copy(src, Dest);
            AssetDatabase.Refresh();
            Debug.Log("[EtpTrace] eterpix-app addon installed files=" + n + " -> " + Dest);
            EditorUtility.DisplayDialog("EterPix-app", n + " 個のファイルを " + Dest + " へコピーしました.\nコンパイル完了後、README の手順 3 以降へ進んでください.", "OK");
        }

        static int Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            int n = 0;
            foreach (string f in Directory.GetFiles(from))
            {
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                n++;
            }
            foreach (string d in Directory.GetDirectories(from))
                n += Copy(d, Path.Combine(to, Path.GetFileName(d)));
            return n;
        }
    }
}
