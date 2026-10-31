using EterPixNudeNyang;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

// EterPixAppGeometry の自己テスト. Test Runner からは Assembly-CSharp を参照できないため、
// 実コードを U# プロキシ経由で直接呼び、結果を [EtpTrace] test PASS/FAIL で出す
public static class EterPixAppGeometrySelfTest
{
    const string ProgramPath = "Assets/EterPixNudeNyang/Scripts/EterPixAppGeometry";
    static int fails;

    [MenuItem("NudeNyang/EterPix-app/幾何の自己テスト")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
        {
            var p = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            p.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
            AssetDatabase.CreateAsset(p, ProgramPath + ".asset");
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Debug.Log("[EtpTrace] test: プログラムアセットを作成しました. コンパイル完了後にもう一度実行してください");
            return;
        }

        var go = new GameObject("GeometrySelfTest") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var g = go.AddUdonSharpComponent<EterPixAppGeometry>();
            fails = 0;

            // セルのuv: copo1=左上, 2=右上, 3=左中, 6=右下
            Rect(g.CellUv(1), 0f, 2f / 3f, 0.5f, 1f / 3f, "cell1");
            Rect(g.CellUv(2), 0.5f, 2f / 3f, 0.5f, 1f / 3f, "cell2");
            Rect(g.CellUv(3), 0f, 1f / 3f, 0.5f, 1f / 3f, "cell3");
            Rect(g.CellUv(6), 0.5f, 0f, 0.5f, 1f / 3f, "cell6");

            // 縦判定
            Bool(g.IsPortrait(0), false, "portrait0");
            Bool(g.IsPortrait(1), true, "portrait1");
            Bool(g.IsPortrait(2), false, "portrait2");
            Bool(g.IsPortrait(3), true, "portrait3");

            // 表示の回転数: 横枠(16:9)と縦枠(9:16)
            Int(g.DisplayTurns(0, 160, 90), 0, "turns coro0 横枠");
            Int(g.DisplayTurns(1, 160, 90), 0, "turns coro1 横枠(縦写真を反時計に90°足して横向きに)");
            Int(g.DisplayTurns(0, 90, 160), 3, "turns coro0 縦枠");
            Int(g.DisplayTurns(1, 90, 160), 1, "turns coro1 縦枠");
            Int(g.DisplayTurns(2, 160, 90), 2, "turns coro2 横枠");
            Int(g.DisplayTurns(3, 160, 90), 2, "turns coro3 横枠");

            // 大きさ: 枠いっぱいに収まる(回した後の寸法が枠以下で、どちらかが一致)
            Fit(g, 160, 90, 0);
            Fit(g, 90, 160, 1);
            Fit(g, 90, 160, 3);
            Fit(g, 200, 100, 0);
            Fit(g, 100, 200, 1);

            // 一覧用の正方形切り出し: セルの中央、幅は 9/16
            Rect sq = g.SquareCropUv(1);
            Rect(sq, (0.5f - 0.28125f) * 0.5f, 2f / 3f, 0.28125f, 1f / 3f, "square1");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
        Debug.Log("[EtpTrace] test geometry " + (fails == 0 ? "PASS" : "FAIL fails=" + fails));
    }

    static void Fit(EterPixAppGeometry g, float fw, float fh, int turns)
    {
        Vector2 s = g.FitSize(fw, fh, turns);
        bool rotated = turns % 2 == 1;
        float w = rotated ? s.y : s.x;
        float h = rotated ? s.x : s.y;
        bool fitsInside = w <= fw + 0.001f && h <= fh + 0.001f;
        bool touches = Mathf.Abs(w - fw) < 0.001f || Mathf.Abs(h - fh) < 0.001f;
        bool aspect = Mathf.Abs(s.x / s.y - 16f / 9f) < 0.001f;
        Bool(fitsInside && touches && aspect, true, "fit " + fw + "x" + fh + " turns=" + turns);
    }

    static void Rect(Rect actual, float x, float y, float w, float h, string label)
    {
        bool ok = Mathf.Abs(actual.x - x) < 0.0001f && Mathf.Abs(actual.y - y) < 0.0001f
            && Mathf.Abs(actual.width - w) < 0.0001f && Mathf.Abs(actual.height - h) < 0.0001f;
        if (!ok) { fails++; Debug.LogError("[EtpTrace] test FAIL " + label + " actual=" + actual); }
    }

    static void Bool(bool actual, bool expected, string label)
    {
        if (actual != expected) { fails++; Debug.LogError("[EtpTrace] test FAIL " + label + " actual=" + actual); }
    }

    static void Int(int actual, int expected, string label)
    {
        if (actual != expected) { fails++; Debug.LogError("[EtpTrace] test FAIL " + label + " actual=" + actual + " expected=" + expected); }
    }
}
