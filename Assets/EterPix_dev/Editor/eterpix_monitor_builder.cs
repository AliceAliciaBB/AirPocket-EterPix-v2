#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using ali.eterpix.v2;

namespace ali.eterpix.dev
{
    // EterPixモニターのprefab(Stack / Split)と EterpixTheme をコードから組み立てる開発用ツール。
    // 寸法は docs/superpowers/plans/2026-09-27-eterpix-monitor-ui-remake.md の表のとおり。
    // prefabを手で直さず、ここを直して作り直すこと(同じパスに上書きするのでGUIDは変わらない)。
    public static class eterpix_monitor_builder
    {
        public const string PrefabDir = "Assets/EterPix/Prefabs";
        public const string StackPath = PrefabDir + "/EterpixMonitor_Stack.prefab";
        public const string SplitPath = PrefabDir + "/EterpixMonitor_Split.prefab";
        public const string ThemePath = PrefabDir + "/EterpixTheme.prefab";
        public const string PreviewScenePath = "Assets/EterPix_dev/Preview/MonitorPreview.unity";

        private const string FontPath = "Assets/EterPix/Fonts/NotoSansJP-Bold SDF.asset";
        private const string LoadingShaderName = "EterPix/UI/LoadingPulse";
        private const string LoadingMaterialPath = "Assets/EterPix/UI/LoadingPulse.mat";
        // Material Symbols Rounded "refresh"(Apache 2.0、UI/MaterialSymbols_LICENSE.txt)を白128pxのPNGにしたもの
        private const string ReloadIconPath = "Assets/EterPix/UI/icon_reload.png";

        private const float W = 560f;
        private const float P = 16f;
        private const float HeaderY = 16f;
        private const float HeaderH = 40f;
        private const float PhotoY = 72f;
        private const float PhotoW = 528f;
        private const float PhotoH = 396f;
        private const float ContentY = 484f;
        private const float StackH = 684f;
        private const float SplitH = 608f;
        private const float HitRatio = 0.35f;
        private const float RootScale = 0.00125f;

        private const string InfoUrl = "https://api.eterpix.uk/app";
        private const string InfoHeading = "EterPixについて";
        private const string InfoBody =
            "EterPixは、VRChatで撮影した写真を共有できるSNSです。このモニターでは、EterPixに投稿された公開写真を閲覧できます。\n\n" +
            "<b>ページを切り替える</b>\n写真の左右を押すと、前後の投稿に切り替わります。表示中のページは、このインスタンスにいる全員で共有されます。\n\n" +
            "<b>ワールドへ行く</b>\n写真にワールド情報がある場合は「ポータルを開く」を押すと、撮影されたワールドへのポータルが開きます。\n\n" +
            "<b>再読み込み</b>\n右上の再読み込みボタンで、投稿の一覧を取り直します。あなたにだけ反映されます。一度押すと、しばらくは押せません。\n\n" +
            "<b>表示色を変える</b>\n右上の切替ボタンで、黒・白などの表示色を切り替えられます。切り替えはあなたにだけ反映され、次に来たときも保持されます(ワールドによっては切り替えできません)。\n\n" +
            "<b>写真を投稿する</b>\n投稿はWebから行えます。下のURLをコピーして、ブラウザで開いてください。";
        private const string InfoFooter = "最新情報は X @_alicilia をご確認ください";

        private static TMP_FontAsset _font;

        [MenuItem("ali/eterpix/dev/Build Monitor Prefabs")]
        public static void BuildAll()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null) throw new System.Exception("Font not found: " + FontPath);
            Material loadingMat = EnsureLoadingMaterial();
            Sprite reloadIcon = EnsureSprite(ReloadIconPath);

            BuildMonitor(false, loadingMat, reloadIcon);
            BuildMonitor(true, loadingMat, reloadIcon);
            BuildThemePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[eterpix_monitor_builder] Built Stack / Split / EterpixTheme prefabs.");
        }

        // ---------------- prefab ----------------

        private static Material EnsureLoadingMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(LoadingMaterialPath);
            if (mat != null) return mat;
            Shader shader = Shader.Find(LoadingShaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + LoadingShaderName);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, LoadingMaterialPath);
            return mat;
        }

        private static Sprite EnsureSprite(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.Exception("Texture not found: " + path);
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new System.Exception("Sprite not found: " + path);
            return sprite;
        }

        private static void BuildThemePrefab()
        {
            GameObject go = new GameObject(eterpix_theme.SingletonObjectName);
            try
            {
                UdonSharpUndo.AddComponent<eterpix_theme>(go);
                PrefabUtility.SaveAsPrefabAsset(go, ThemePath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void BuildMonitor(bool split, Material loadingMat, Sprite reloadIcon)
        {
            float h = split ? SplitH : StackH;
            GameObject root = new GameObject(split ? "EterpixMonitor_Split" : "EterpixMonitor_Stack");
            try
            {
                root.transform.localScale = Vector3.one * RootScale;
                eterpix_monitor monitor = UdonSharpUndo.AddComponent<eterpix_monitor>(root);
                eterpix_monitor_theme monitorTheme = UdonSharpUndo.AddComponent<eterpix_monitor_theme>(root);
                UdonBehaviour monitorUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(monitor);
                UdonBehaviour themeUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(monitorTheme);

                // ---- Screen (Canvas)。pivotを上端中央にして、旧prefab(ルートがCanvas)と同じ位置に来るようにする
                GameObject screen = NewUI("Screen", root.transform);
                Canvas canvas = screen.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                screen.AddComponent<GraphicRaycaster>();
                screen.AddComponent<VRCUiShape>();
                RectTransform srt = (RectTransform)screen.transform;
                srt.anchorMin = new Vector2(0.5f, 0.5f);
                srt.anchorMax = new Vector2(0.5f, 0.5f);
                srt.pivot = new Vector2(0.5f, 1f);
                srt.sizeDelta = new Vector2(W, h);
                srt.localPosition = Vector3.zero;
                srt.localRotation = Quaternion.identity;
                srt.localScale = Vector3.one;

                GameObject bg = NewUI("bg_Background", screen.transform);
                Stretch(bg, 0, 0, 0, 0);
                AddImage(bg, Color.black, true);

                // ---- Header
                GameObject header = NewUI("Header", screen.transform);
                Place(header, P, HeaderY, PhotoW, HeaderH);

                GameObject logo = NewUI("tx_Logo", header.transform);
                Place(logo, 0, 0, 220, HeaderH);
                AddText(logo, "EterPix-Beta", 16, TextAlignmentOptions.MidlineLeft, false);

                GameObject page = NewUI("tx_PageLabel", header.transform);
                Place(page, PhotoW / 2f - 80f, 0, 160, HeaderH);
                TextMeshProUGUI pageText = AddText(page, "1 / 12", 16, TextAlignmentOptions.Midline, false);

                GameObject themeBtn = NewUI("btn_ThemeButton", header.transform);
                // テーマ切替できないワールドでは非表示になるため、左端に置いて抜けても隙間が目立たないようにする
                Place(themeBtn, PhotoW - 136f, 0, 40, 40);
                Button themeButton = AddButton(themeBtn, AddImage(themeBtn, Color.gray, true), true);
                BindEvent(themeButton, themeUdon, "CycleTheme");
                // 左半分が塗り、右半分が枠だけの20x20の正方形(長方形4枚)
                IconRect(themeBtn, "tx_ThemeIconFill", 10, 10, 10, 20);
                IconRect(themeBtn, "tx_ThemeIconTop", 20, 10, 10, 2);
                IconRect(themeBtn, "tx_ThemeIconBottom", 20, 28, 10, 2);
                IconRect(themeBtn, "tx_ThemeIconRight", 28, 10, 2, 20);

                GameObject reloadBtn = NewUI("btn_ReloadButton", header.transform);
                Place(reloadBtn, PhotoW - 88f, 0, 40, 40);
                Button reloadButton = AddButton(reloadBtn, AddImage(reloadBtn, Color.gray, true), true);
                BindEvent(reloadButton, monitorUdon, "ReloadFeed");
                GameObject reloadIconGo = NewUI("tx_ReloadIcon", reloadBtn.transform);
                Place(reloadIconGo, 8, 8, 24, 24);
                AddImage(reloadIconGo, Color.white, false).sprite = reloadIcon;

                GameObject infoBtn = NewUI("btn_InfoButton", header.transform);
                Place(infoBtn, PhotoW - 40f, 0, 40, 40);
                Button infoButton = AddButton(infoBtn, AddImage(infoBtn, Color.gray, true), true);
                BindEvent(infoButton, monitorUdon, "ToggleInformationWindow");
                GameObject iconOpen = NewUI("tx_InfoIconOpen", infoBtn.transform);
                Stretch(iconOpen, 0, 0, 0, 0);
                AddText(iconOpen, "i", 18, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;
                GameObject iconClose = NewUI("InfoIconClose", infoBtn.transform);
                Stretch(iconClose, 0, 0, 0, 0);
                CrossBar(iconClose, "tx_InfoIconCloseA", 45f);
                CrossBar(iconClose, "tx_InfoIconCloseB", -45f);
                iconClose.SetActive(false);

                // ---- Photo
                GameObject frame = NewUI("PhotoFrame", screen.transform);
                Place(frame, P, PhotoY, PhotoW, PhotoH);

                // AspectRatioFitterの親にLayoutGroupを付けないこと(CLAUDE.md aspect_ratio_fitter_under_layout_group)
                GameObject fitterGo = NewUI("ImageFitter", frame.transform);
                Stretch(fitterGo, 0, 0, 0, 0);
                AspectRatioFitter fitter = fitterGo.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = 16f / 9f;

                GameObject postImgGo = NewUI("PostImage", fitterGo.transform);
                RectTransform prt = (RectTransform)postImgGo.transform;
                prt.anchorMin = new Vector2(0.5f, 0.5f);
                prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.sizeDelta = new Vector2(PhotoW, PhotoW * 9f / 16f);
                prt.anchoredPosition = Vector2.zero;
                RawImage raw = postImgGo.AddComponent<RawImage>();
                raw.texture = null;
                raw.material = loadingMat;
                raw.color = Color.gray;
                raw.raycastTarget = false;

                GameObject status = NewUI("StatusRoot", frame.transform);
                Stretch(status, 0, 0, 0, 0);
                GameObject statusTextGo = NewUI("tx_StatusText", status.transform);
                Stretch(statusTextGo, 24, 24, 24, 24);
                TextMeshProUGUI statusText = AddText(statusTextGo, "読み込み中…", 16, TextAlignmentOptions.Center, true);
                status.SetActive(false);

                // ---- ページ送り: 見た目は左右の余白の < >、判定は余白 + 写真の左右35%(写真の上は透明)
                float hitW = PhotoW * HitRatio;
                GameObject prev = NewUI("PrevHit", screen.transform);
                Place(prev, 0, PhotoY, P + hitW, PhotoH);
                Button prevButton = AddButton(prev, AddImage(prev, new Color(1f, 1f, 1f, 0f), true), false);
                BindEvent(prevButton, monitorUdon, "PagePrev");
                GameObject prevGlyph = NewUI("tx_PrevGlyph", prev.transform);
                Place(prevGlyph, 0, 0, P, PhotoH);
                AddText(prevGlyph, "<", 16, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;

                GameObject next = NewUI("NextHit", screen.transform);
                Place(next, W - P - hitW, PhotoY, P + hitW, PhotoH);
                Button nextButton = AddButton(next, AddImage(next, new Color(1f, 1f, 1f, 0f), true), false);
                BindEvent(nextButton, monitorUdon, "PageNext");
                GameObject nextGlyph = NewUI("tx_NextGlyph", next.transform);
                Place(nextGlyph, hitW, 0, P, PhotoH);
                AddText(nextGlyph, ">", 16, TextAlignmentOptions.Center, false).overflowMode = TextOverflowModes.Overflow;

                // ---- 投稿 / ワールド
                TextMeshProUGUI userName, description, worldName, worldDescription;
                GameObject worldRoot, postRoot;
                Button portalButton;
                if (split)
                {
                    GameObject row = NewUI("BottomRow", screen.transform);
                    Place(row, P, ContentY, PhotoW, 108);
                    HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = 16;
                    hlg.childAlignment = TextAnchor.UpperLeft;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = true;
                    hlg.padding = new RectOffset(0, 0, 0, 0);
                    postRoot = row;

                    GameObject postCol = NewUI("PostColumn", row.transform);
                    Column(postCol, 3);
                    userName = AddText(TopStretch(NewUI("tx_UserName", postCol.transform), 0, 24), "投稿者名", 16, TextAlignmentOptions.MidlineLeft, false);
                    description = AddText(TopStretch(NewUI("tx_PostDescription", postCol.transform), 28, 80), "投稿の説明文がここに入ります。#タグ", 13, TextAlignmentOptions.TopLeft, true);

                    worldRoot = NewUI("WorldColumn", row.transform);
                    Column(worldRoot, 2);
                    worldName = AddText(TopStretch(NewUI("tx_WorldName", worldRoot.transform), 0, 24), "ワールド名", 16, TextAlignmentOptions.MidlineLeft, false);
                    worldDescription = AddText(TopStretch(NewUI("tx_WorldDescription", worldRoot.transform), 28, 40), "ワールドの説明文がここに入ります。", 13, TextAlignmentOptions.TopLeft, true);
                    GameObject portalGo = TopStretch(NewUI("acc_PortalButton", worldRoot.transform), 72, 36);
                    portalButton = BuildPortalButton(portalGo, monitorUdon);
                }
                else
                {
                    postRoot = NewUI("PostBlock", screen.transform);
                    Place(postRoot, P, ContentY, PhotoW, 88);
                    GameObject un = NewUI("tx_UserName", postRoot.transform);
                    Place(un, 0, 0, PhotoW, 24);
                    userName = AddText(un, "投稿者名", 16, TextAlignmentOptions.MidlineLeft, false);
                    GameObject pd = NewUI("tx_PostDescription", postRoot.transform);
                    Place(pd, 0, 28, PhotoW, 60);
                    description = AddText(pd, "投稿の説明文がここに入ります。#タグ", 13, TextAlignmentOptions.TopLeft, true);

                    worldRoot = NewUI("WorldBlock", screen.transform);
                    Place(worldRoot, P, 588, PhotoW, 80);
                    GameObject wn = NewUI("tx_WorldName", worldRoot.transform);
                    Place(wn, 0, 0, PhotoW - 156f, 36);
                    worldName = AddText(wn, "ワールド名", 16, TextAlignmentOptions.MidlineLeft, false);
                    GameObject portalGo = NewUI("acc_PortalButton", worldRoot.transform);
                    Place(portalGo, PhotoW - 140f, 0, 140, 36);
                    portalButton = BuildPortalButton(portalGo, monitorUdon);
                    GameObject wd = NewUI("tx_WorldDescription", worldRoot.transform);
                    Place(wd, 0, 40, PhotoW, 40);
                    worldDescription = AddText(wd, "ワールドの説明文がここに入ります。", 13, TextAlignmentOptions.TopLeft, true);
                }

                // ---- 情報ウィンドウ(ヘッダーより下の全体を覆う。最後に置いて最前面に描く)
                GameObject info = NewUI("bg_InfoWindow", screen.transform);
                Place(info, 0, HeaderY + HeaderH, W, h - (HeaderY + HeaderH));
                AddImage(info, Color.black, true);
                BuildInfoContent(info);
                info.SetActive(false);

                // ---- ポータル出現位置(旧prefabと同じ値)。表示範囲の判定はEterpixRequesterのトリガーColliderで行う
                GameObject spawn = new GameObject("PortalSpawnPoint");
                spawn.transform.SetParent(root.transform, false);
                spawn.transform.localPosition = new Vector3(-233f, -548f, 0f);

                // ---- monitor の参照
                SerializedObject so = new SerializedObject(monitor);
                SetRef(so, "image", raw);
                SetRef(so, "figureAspectFitter", fitter);
                SetRef(so, "prevButton", prevButton);
                SetRef(so, "nextButton", nextButton);
                SetRef(so, "pageLabel", pageText);
                SetRef(so, "userNameText", userName);
                SetRef(so, "descriptionText", description);
                SetRef(so, "worldContextRoot", worldRoot);
                SetRef(so, "worldNameText", worldName);
                SetRef(so, "worldDescriptionText", worldDescription);
                SetRef(so, "openPortalButton", portalButton);
                SetRef(so, "portalSpawnPoint", spawn.transform);
                SetRef(so, "reloadButton", reloadButton);
                SetRef(so, "informationButton", infoButton);
                SetRef(so, "informationWindowRoot", info);
                SetRef(so, "infoIconOpen", iconOpen);
                SetRef(so, "infoIconClose", iconClose);
                SetRef(so, "statusRoot", status);
                SetRef(so, "statusText", statusText);
                SetRef(so, "loadingMaterial", loadingMat);
                SetRef(so, "monitorTheme", monitorTheme);
                SetArray(so, "normalOnlyObjects", new Object[] { page, prev, next, fitterGo, postRoot });
                so.FindProperty("debugIgnoreTriggerRange").boolValue = false; // 範囲の判定はEterpixRequesterで行う
                so.ApplyModifiedPropertiesWithoutUndo();

                // ---- 配色(黒プリセットで焼き込む)
                monitorTheme.EditorCollectFromChildren();
                monitorTheme.EditorApplyPreset(eterpix_theme.ThemeBlack);

                PrefabUtility.SaveAsPrefabAsset(root, split ? SplitPath : StackPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Button BuildPortalButton(GameObject go, UdonBehaviour monitorUdon)
        {
            Button b = AddButton(go, AddImage(go, Color.magenta, true), true);
            BindEvent(b, monitorUdon, "OpenPortal");
            GameObject label = NewUI("acctx_PortalLabel", go.transform);
            Stretch(label, 0, 0, 0, 0);
            AddText(label, "ポータルを開く", 16, TextAlignmentOptions.Center, false);
            return b;
        }

        private static void BuildInfoContent(GameObject info)
        {
            GameObject scroll = NewUI("InfoScroll", info.transform);
            Stretch(scroll, P, P, P, P);
            AddImage(scroll, new Color(1f, 1f, 1f, 0f), true);
            scroll.AddComponent<RectMask2D>();
            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
            sr.viewport = (RectTransform)scroll.transform;

            GameObject content = NewUI("InfoContent", scroll.transform);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sr.content = crt;

            AddText(NewUI("tx_InfoHeading", content.transform), InfoHeading, 18, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
            AddText(NewUI("tx_InfoBody", content.transform), InfoBody, 13, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
            BuildUrlField(content.transform);
            AddText(NewUI("tx_InfoFooter", content.transform), InfoFooter, 13, TextAlignmentOptions.TopLeft, true).overflowMode = TextOverflowModes.Overflow;
        }

        // 読み取り専用のURL欄(VRChatはワールドからブラウザを開けないため、コピーしてもらう)
        private static void BuildUrlField(Transform parent)
        {
            GameObject go = NewUI("btn_UrlField", parent);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;
            Image img = AddImage(go, Color.gray, true);

            GameObject area = NewUI("TextArea", go.transform);
            Stretch(area, 8, 0, 8, 0);
            area.AddComponent<RectMask2D>();

            GameObject textGo = NewUI("tx_UrlText", area.transform);
            Stretch(textGo, 0, 0, 0, 0);
            TextMeshProUGUI t = AddText(textGo, InfoUrl, 13, TextAlignmentOptions.MidlineLeft, false);
            t.overflowMode = TextOverflowModes.Overflow;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = (RectTransform)area.transform;
            field.textComponent = t;
            field.targetGraphic = img;
            field.fontAsset = _font;
            field.pointSize = 13;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.readOnly = true;
            field.transition = Selectable.Transition.None;
            Navigation nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;
            field.text = InfoUrl;
        }

        // ---------------- template migration ----------------
        private const string TemplatePath = "Assets/EterPix/Etp_テンプレート.prefab";
        private const string LegacyMonitorPath = "Assets/EterPix/Prefabs/Legacy/EterpixMonitorUI.prefab";

        // テンプレート内の旧モニター(EterpixMonitorUI)を Stack に差し替え、EterpixTheme を追加する。
        // 位置・回転・スケール・アクティブ状態・offset を引き継ぐ。
        [MenuItem("ali/eterpix/dev/Migrate Template To Stack")]
        public static void MigrateTemplate()
        {
            GameObject stack = AssetDatabase.LoadAssetAtPath<GameObject>(StackPath);
            GameObject themePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThemePath);
            GameObject legacy = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyMonitorPath);
            if (stack == null || themePrefab == null || legacy == null) throw new System.Exception("Build prefabs and move legacy prefab first.");

            GameObject contents = PrefabUtility.LoadPrefabContents(TemplatePath);
            try
            {
                List<GameObject> olds = new List<GameObject>();
                foreach (Transform t in contents.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source == legacy) olds.Add(go);
                }

                int replaced = 0;
                foreach (GameObject old in olds)
                {
                    Transform parent = old.transform.parent;
                    int sibling = old.transform.GetSiblingIndex();
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(stack, parent);
                    inst.name = old.name.Replace("EterpixMonitorUI", "EterpixMonitor_Stack");
                    inst.transform.SetSiblingIndex(sibling);
                    inst.transform.localPosition = old.transform.localPosition;
                    inst.transform.localRotation = old.transform.localRotation;
                    inst.transform.localScale = old.transform.localScale;
                    inst.SetActive(old.activeSelf);

                    SerializedObject oldSo = new SerializedObject(old.GetComponent<eterpix_monitor>());
                    SerializedObject newSo = new SerializedObject(inst.GetComponent<eterpix_monitor>());
                    newSo.FindProperty("offset").intValue = oldSo.FindProperty("offset").intValue;
                    newSo.ApplyModifiedPropertiesWithoutUndo();

                    Object.DestroyImmediate(old);
                    replaced++;
                }

                if (contents.transform.Find(eterpix_theme.SingletonObjectName) == null)
                {
                    GameObject theme = (GameObject)PrefabUtility.InstantiatePrefab(themePrefab, contents.transform);
                    theme.name = eterpix_theme.SingletonObjectName;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, TemplatePath);
                Debug.Log("[eterpix_monitor_builder] Template migrated. replaced=" + replaced);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ---------------- preview ----------------
        // 見た目確認用。Stack(上段)/ Split(下段)× 5状態を並べる。
        // 今開いているシーンを閉じないよう、Additiveで開く(保存はこのシーンだけ)。
        [MenuItem("ali/eterpix/dev/Build Monitor Preview Scene")]
        public static void BuildPreviewScene()
        {
            CloseMonitorPreviewScene();
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            GameObject rootGo = new GameObject("MonitorPreview");
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            rootGo.transform.position = new Vector3(1000f, 1000f, 0f);

            GameObject stack = AssetDatabase.LoadAssetAtPath<GameObject>(StackPath);
            GameObject split = AssetDatabase.LoadAssetAtPath<GameObject>(SplitPath);
            string[] labels = { "Normal-Black", "Normal-White", "Untrusted-Black", "Info-White", "NoWorld-Black" };
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < labels.Length; col++)
                {
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(row == 0 ? stack : split, rootGo.transform);
                    inst.name = (row == 0 ? "Stack_" : "Split_") + labels[col];
                    inst.transform.localPosition = new Vector3(col * 0.9f, row == 0 ? 0f : -1.0f, 0f);
                    ApplyPreviewState(inst, col);
                }
            }

            GameObject camGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            camGo.transform.position = new Vector3(1000f + 2.15f, 1000f - 0.93f, -2f);
            camGo.transform.rotation = Quaternion.identity;
            cam.enabled = false; // 他のシーンの表示を邪魔しない。撮影時は camera 指定で描画される

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            Debug.Log("[eterpix_monitor_builder] Preview scene built: " + PreviewScenePath);
        }

        [MenuItem("ali/eterpix/dev/Close Monitor Preview Scene")]
        public static void CloseMonitorPreviewScene()
        {
            Scene existing = SceneManager.GetSceneByPath(PreviewScenePath);
            if (existing.IsValid() && existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
        }

        // 0: 通常(黒) 1: 通常(白) 2: 信頼されていないURL(黒) 3: 情報ウィンドウ(白) 4: ワールド無し(黒)
        private static void ApplyPreviewState(GameObject inst, int state)
        {
            eterpix_monitor_theme theme = inst.GetComponent<eterpix_monitor_theme>();
            eterpix_monitor monitor = inst.GetComponent<eterpix_monitor>();
            SerializedObject so = new SerializedObject(monitor);
            bool white = state == 1 || state == 3;
            theme.EditorApplyPreset(white ? eterpix_theme.ThemeWhite : eterpix_theme.ThemeBlack);

            // 写真の代わりに中間のグレーを置く(明滅マテリアルを外す)
            RawImage raw = (RawImage)so.FindProperty("image").objectReferenceValue;
            raw.material = null;
            raw.color = new Color(0.45f, 0.45f, 0.45f, 1f);

            if (state == 2)
            {
                GameObject status = (GameObject)so.FindProperty("statusRoot").objectReferenceValue;
                TMP_Text statusText = (TMP_Text)so.FindProperty("statusText").objectReferenceValue;
                status.SetActive(true);
                statusText.text = eterpix_monitor.UntrustedUrlMessage;
                SerializedProperty normal = so.FindProperty("normalOnlyObjects");
                for (int i = 0; i < normal.arraySize; i++) ((GameObject)normal.GetArrayElementAtIndex(i).objectReferenceValue).SetActive(false);
                ((GameObject)so.FindProperty("worldContextRoot").objectReferenceValue).SetActive(false);
            }
            if (state == 3)
            {
                ((GameObject)so.FindProperty("informationWindowRoot").objectReferenceValue).SetActive(true);
                ((GameObject)so.FindProperty("infoIconOpen").objectReferenceValue).SetActive(false);
                ((GameObject)so.FindProperty("infoIconClose").objectReferenceValue).SetActive(true);
            }
            if (state == 4)
            {
                ((GameObject)so.FindProperty("worldContextRoot").objectReferenceValue).SetActive(false);
            }
            Canvas.ForceUpdateCanvases();
        }

        // ---------------- helpers ----------------

        private static GameObject NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 0;
            go.transform.SetParent(parent, false);
            return go;
        }

        // 親の左上を原点(下向き+y)にして配置する
        private static RectTransform Place(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static RectTransform Stretch(GameObject go, float left, float top, float right, float bottom)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // 親の幅いっぱい・上端からyの位置に高さhで置く
        private static GameObject TopStretch(GameObject go, float y, float h)
        {
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            rt.sizeDelta = new Vector2(0f, h);
            return go;
        }

        private static void Column(GameObject go, float flexibleWidth)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = 0;
            le.preferredWidth = 0;
            le.flexibleWidth = flexibleWidth;
        }

        private static Image AddImage(GameObject go, Color color, bool raycast)
        {
            Image img = go.AddComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static void IconRect(GameObject parent, string name, float x, float y, float w, float h)
        {
            GameObject go = NewUI(name, parent.transform);
            Place(go, x, y, w, h);
            AddImage(go, Color.white, false);
        }

        private static void CrossBar(GameObject parent, string name, float angle)
        {
            GameObject go = NewUI(name, parent.transform);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(22f, 3f);
            rt.anchoredPosition = Vector2.zero;
            rt.localEulerAngles = new Vector3(0f, 0f, angle);
            AddImage(go, Color.white, false);
        }

        private static TextMeshProUGUI AddText(GameObject go, string text, float size, TextAlignmentOptions align, bool wrap)
        {
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.enableAutoSizing = false;
            t.fontStyle = FontStyles.Normal;
            t.color = Color.white;
            t.text = text;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = true;
            t.margin = Vector4.zero;
            return t;
        }

        private static Button AddButton(GameObject go, Image target, bool tint)
        {
            Button b = go.AddComponent<Button>();
            b.targetGraphic = target;
            Navigation nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            if (tint)
            {
                // 押下の色はテーマに依らず、塗りを暗くするだけ(U#から ColorBlock を書き換えないため)
                ColorBlock cb = ColorBlock.defaultColorBlock;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                cb.selectedColor = Color.white;
                cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.1f;
                b.transition = Selectable.Transition.ColorTint;
                b.colors = cb;
            }
            else
            {
                b.transition = Selectable.Transition.None;
            }
            return b;
        }

        private static void BindEvent(Button button, UdonBehaviour udon, string eventName)
        {
            UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent), eventName);
        }

        private static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.Exception("eterpix_monitor has no field: " + name);
            p.objectReferenceValue = value;
        }

        private static void SetArray(SerializedObject so, string name, Object[] values)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.Exception("eterpix_monitor has no field: " + name);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
