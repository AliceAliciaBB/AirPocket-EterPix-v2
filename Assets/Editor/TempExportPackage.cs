using UnityEditor;
public class TempExportPackage
{
    [MenuItem("Tools/Export EterPix 20261002")]
    public static void Export()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var path = "V:/Etp-vrc-gimmick/20261002-etp-vrc.unitypackage";
        AssetDatabase.ExportPackage("Assets/EterPix", path, ExportPackageOptions.Recurse);
        UnityEngine.Debug.Log(System.IO.File.Exists(path) ? "ok " + new System.IO.FileInfo(path).Length : "missing");
    }
}
