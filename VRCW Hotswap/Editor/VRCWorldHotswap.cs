#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
#if VRC_SDK_VRCSDK3
using VRC.Core;
using VRC.SDK3.Components;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.Api;
#endif

[InitializeOnLoad]
public class VRCWorldHotswap
{
    public const string Version = "1.0.0-beta";
    public const string TestedWorldsSdkVersion = "3.10.4";
    public const string TestedUnityVersion = "2022.3.22f1";

    public const string TestedUnityVersion2 = "2022.3.6f1";
    public const string TestedWorldsSdkVersion2 = "3.7.6";
    public const string MaintainerName = "thebigbaddawg";
    public const string MaintainerUrl = "https://github.com/thebigbaddawg";
    public const string OriginalAuthorName = "FACS01";
    public const string OriginalAuthorUrl = "https://github.com/FACS01-01";

    private static readonly string ProjTempPath = Application.temporaryCachePath;
    private static readonly string DecompRecoveredPath = ProjTempPath + "/decomp_world.vrcw";
    private static readonly string DecompModPath = ProjTempPath + "/decomp_world_mod.vrcw";
    private static readonly string TmpOutPath = ProjTempPath + "/hotswap_world_out.vrcw";
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private const string SessionLastBuildPathKey = "VRC.SDK3.Editor_patToLastBuild";
    private const string SessionHotswapActiveKey = "VRCWorldHotswap.Active";
    private const string SessionHotswapDestKey = "VRCWorldHotswap.DestPath";
    private const string SessionHotswapSizeKey = "VRCWorldHotswap.DestSize";
    private const string SessionHotswapFpKey = "VRCWorldHotswap.DestFp";
    private const string PrefsSeenHowtoKey = "VRCWorldHotswap.SeenHowto";
    private const string WorldIdPattern = @"wrld_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
    private const string BuildPlayerPattern = @"BuildPlayer-[^\x00\r\n\.]{1,128}";
    private const string UnityVerPattern = @"20[\d]{2}\.[\d]\.[\d]{1,2}f[\d]";

    private const long PcUploadMaxBytes = 1024L * 1024 * 1024;
    private const long PcUploadUnlikelyBytes = (long)(1.5 * 1024 * 1024 * 1024);
    private const long PcUploadHopelessBytes = (long)(2.5 * 1024 * 1024 * 1024);

    private const long AndroidUploadMaxBytes = 100L * 1024 * 1024;
    private const long AndroidUploadUnlikelyBytes = 100L * 1024 * 1024;
    private const long AndroidUploadHopelessBytes = 100L * 1024 * 1024;
    private const long AndroidPracticalSourceMaxBytes = 100L * 1024 * 1024;
    private const long AndroidTrySourceMaxBytes = 200L * 1024 * 1024;

    private static bool IsAndroidBuildTarget =>
    EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;

    private static long WorldUploadMaxBytes =>
    IsAndroidBuildTarget ? AndroidUploadMaxBytes : PcUploadMaxBytes;

    private static long WorldUploadUnlikelyBytes =>
    IsAndroidBuildTarget ? AndroidUploadUnlikelyBytes : PcUploadUnlikelyBytes;

    private static long WorldUploadHopelessBytes =>
    IsAndroidBuildTarget ? AndroidUploadHopelessBytes : PcUploadHopelessBytes;

    private static string UploadPlatformLabel =>
    IsAndroidBuildTarget ? "Android" : "PC";

    private const string AndroidUploadDisclaimer =
    "Android hotswap is barely tested (Quest, Pico, phones, etc.).\n" +
    "It may not work. Android worlds must be under 100 MB after packing.";

    private static AssetBundleRecompressOperation abro;
    private static AssetBundleCreateRequest abcr;
    private static string pendingRecoveredPath;
    private static string pendingOutputPath;
    private static string pendingNewWorldId;

    private static string activeUncompressedRecoveredPath;
    private static bool operationBusy;
    private static bool cancelRequested;

    private static bool suppressUploadResultDialog;
    private static int asyncOpSerial;
    private static string uploadProgressStatus = "Uploading...";
    private static float uploadProgress01;
    private static CancellationTokenSource uploadCts;
#if VRC_SDK_VRCSDK3
    private static IVRCSdkWorldBuilderApi activeUploadBuilder;
#endif

    private const uint UnityFsCompressionTypeMask = 0x3Fu;
    private const uint UnityFsBlocksInfoAtTheEnd = 0x80u;
    private const uint UnityFsBlockInfoNeedPaddingAtStart = 0x200u;

    static VRCWorldHotswap()
    {
        operationBusy = false;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }

    private static void OnBeforeAssemblyReload()
    {
        operationBusy = false;
        cancelRequested = false;
        asyncOpSerial++;
        try { uploadCts?.Cancel(); } catch { }
        try { uploadCts?.Dispose(); } catch { }
        uploadCts = null;
#if VRC_SDK_VRCSDK3
        activeUploadBuilder = null;
#endif
        try { EditorUtility.ClearProgressBar(); } catch { }
    }

    public static string HowtoDialogBody =>
    "How to use:\n\n" +
    "1) Open a simple world scene\n" +
    " (or VRCW Hotswap > Spawn Dummy World)\n\n" +
    "2) In the VRChat SDK, click Build & Publish\n" +
    " (the default button is fine)\n\n" +
    "3) VRCW Hotswap > Load Hotswap File (.vrcw)\n" +
    " and pick your world\n\n" +
    "4) VRCW Hotswap > Upload Hotswapped Build\n\n" +
    "After step 3, do NOT click Build & Publish again.\n" +
    "That rebuilds the scene and undoes the swap.\n\n" +
    "Your original .vrcw is not changed.";

    public static void ShowHowtoDialog()
    {
        EditorUtility.DisplayDialog("VRCW Hotswap", HowtoDialogBody, "Ok");
        EditorPrefs.SetBool(PrefsSeenHowtoKey, true);
    }

    public static void ResetHowtoPref()
    {
        EditorPrefs.DeleteKey(PrefsSeenHowtoKey);
    }

    [MenuItem("VRCW Hotswap/Load Hotswap File (.vrcw)", true)]
    private static bool ValidateHotswap()
    {
#if !VRC_SDK_VRCSDK3
        return false;
#else
        return !operationBusy && !EditorApplication.isPlayingOrWillChangePlaymode;
#endif
    }

    [MenuItem("VRCW Hotswap/Load Hotswap File (.vrcw)", false, 1)]
    public static void Hotswap()
    {
#if !VRC_SDK_VRCSDK3
        EditorUtility.DisplayDialog("VRCW Hotswap", "VRChat World SDK is not in this project.", "Ok");
        return;
#else
        if (!TryBeginOperation("Hotswap"))
        return;

        if (SessionState.GetBool(SessionHotswapActiveKey, false))
        {
            if (!EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "You already loaded a world.\n\n" +
            "Load a different one instead?\n" +
            "This clears the current swap first.",
            "Load New File",
            "Cancel"))
            {
                Debug.LogWarning("VRCW Hotswap cancelled (kept existing hotswap session).\n");
                EndOperation();
                return;
            }

            ClearPriorHotswapForReload();
            Debug.Log("<color=cyan>VRCW Hotswap:</color> cleared old swap before loading a new file.\n");
        }

        bool showHowto = !EditorPrefs.GetBool(PrefsSeenHowtoKey, false);
        if (showHowto)
        {
            if (!EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            HowtoDialogBody + "\n\nContinue?",
            "Continue",
            "Cancel"))
            {
                Debug.LogWarning("VRCW Hotswap cancelled.\n");
                EndOperation();
                return;
            }
            EditorPrefs.SetBool(PrefsSeenHowtoKey, true);
        }

        var pipeline = FindScenePipelineManager();
        if (pipeline == null)
        {
            EditorUtility.DisplayDialog("VRCW Hotswap",
            "This scene has no world setup.\n\nUse Spawn Dummy World, or open a world scene.",
            "Ok");
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        if (string.IsNullOrWhiteSpace(pipeline.blueprintId))
        {
            EditorUtility.DisplayDialog("VRCW Hotswap",
            "This scene has no world ID yet.\n\nClick Build & Publish in the VRChat SDK first, then try again.",
            "Ok");
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        pendingNewWorldId = pipeline.blueprintId.Trim();
        if (!Regex.IsMatch(pendingNewWorldId, "^" + WorldIdPattern + "$"))
        {
            EditorUtility.DisplayDialog("VRCW Hotswap",
            $"Bad world ID on this scene:\n{pendingNewWorldId}",
            "Ok");
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        string lastBuild = SessionState.GetString(SessionLastBuildPathKey, null);
        if (string.IsNullOrEmpty(lastBuild) || !File.Exists(lastBuild))
        {
            if (!EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "No SDK build found yet.\n\nClick Build & Publish in the VRChat SDK first.\n\nContinue anyway? You can still save a file by hand.",
            "Continue Anyway",
            "Cancel"))
            {
                Debug.LogWarning("VRCW Hotswap cancelled (no last build).\n");
                EndOperation();
                return;
            }
            pendingOutputPath = null;
        }
        else
        {
            pendingOutputPath = lastBuild;
            Debug.Log($"Using SDK build file:\n{lastBuild}\n");
        }

        string vrcwPath = EditorUtility.OpenFilePanelWithFilters(
        "Pick the .vrcw to hotswap",
        Application.dataPath,
        new[] { "World Files", "vrcw", "All files", "*" });

        if (string.IsNullOrEmpty(vrcwPath))
        {
            Debug.LogWarning("VRCW Hotswap cancelled.\n");
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        pendingRecoveredPath = vrcwPath;
        Debug.Log($"Selected .vrcw (not changed):\n{vrcwPath}\nNew world ID:\n{pendingNewWorldId}\n");

        if (!ConfirmPlatformMatchOrContinue(vrcwPath))
        {
            EndOperation();
            return;
        }

        if (!ConfirmUnityVersionOrContinue(vrcwPath))
        {
            EndOperation();
            return;
        }

        if (!ConfirmSourceSizeOrContinue(vrcwPath))
        {
            EndOperation();
            return;
        }

        CleanupTempFiles();
        PrepareUncompressedVrcw(
        vrcwPath,
        DecompRecoveredPath,
        "VRCW Hotswap",
        "Preparing your world file...",
        AbroProgressRecovered,
        readyPath =>
        {
            if (ConsumeCancelIfRequested("Hotswap cancelled."))
            return;
            activeUncompressedRecoveredPath = readyPath;
            AnalyzeAndRewrite();
        },
        onFailed: EndOperation);
#endif
    }

    [MenuItem("VRCW Hotswap/Upload Hotswapped Build", true)]
    private static bool ValidateUploadLastBuild()
    {
#if !VRC_SDK_VRCSDK3
        return false;
#else

        return !operationBusy &&
        !EditorApplication.isPlayingOrWillChangePlaymode &&
        (HasActiveHotswapReady() || SessionState.GetBool(SessionHotswapActiveKey, false));
#endif
    }

    [MenuItem("VRCW Hotswap/Upload Hotswapped Build", false, 2)]
    public static void UploadLastBuildMenu()
    {
#if !VRC_SDK_VRCSDK3
        EditorUtility.DisplayDialog("VRCW Hotswap", "VRChat World SDK is not in this project.", "Ok");
        return;
#else
        if (operationBusy)
        {
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "Something else is already running.\n\nWait for it to finish, then try again.",
            "Ok");
            return;
        }

        if (!HasActiveHotswapReady())
        {
            if (SessionState.GetBool(SessionHotswapActiveKey, false))
            {
                string dest = SessionState.GetString(SessionHotswapDestKey, null);
                if (string.IsNullOrEmpty(dest))
                dest = SessionState.GetString(SessionLastBuildPathKey, null);

                TryValidateHotswapIntegrity(dest ?? "", clearSessionOnMismatch: true);
                return;
            }

            EditorUtility.DisplayDialog("VRCW Hotswap",
            "Nothing ready to upload.\n\nLoad a .vrcw first (Load Hotswap File).",
            "Ok");
            return;
        }
        UploadLastBuildAsync();
#endif
    }

    [MenuItem("VRCW Hotswap/About VRCW Hotswap", false, 50)]
    public static void OpenAbout()
    {
        VRCWorldHotswapAboutWindow.ShowWindow();
    }

#if VRC_SDK_VRCSDK3
    private static async void UploadLastBuildAsync()
    {

        if (!TryBeginOperation("Upload"))
        return;

        IVRCSdkWorldBuilderApi builder = null;
        EventHandler<(string status, float percentage)> progressHandler = null;
        EventHandler uploadStartHandler = null;

        try
        {
            string lastBuild = SessionState.GetString(SessionLastBuildPathKey, null);
            if (string.IsNullOrEmpty(lastBuild) || !File.Exists(lastBuild))
            {
                EditorUtility.DisplayDialog("VRCW Hotswap",
                "Nothing ready to upload.\n\nLoad a .vrcw first, then try again.",
                "Ok");
                return;
            }

            if (!VRCSdkControlPanel.TryGetBuilder(out builder))
            {
                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                EditorUtility.DisplayDialog("VRCW Hotswap",
                "Open the VRChat SDK Control Panel.\n\n" +
                "Sign in, go to Builder, fill in name / description / image, then try Upload again.\n\n" +
                "Don't click Build & Publish after a hotswap.",
                "Ok");
                return;
            }

            if (builder.UploadState == SdkUploadState.Uploading)
            {
                EditorUtility.DisplayDialog(
                "VRCW Hotswap",
                "VRChat is already uploading something.\n\nWait, then try again.",
                "Ok");
                return;
            }

            var pipeline = FindScenePipelineManager();
            if (pipeline == null || string.IsNullOrWhiteSpace(pipeline.blueprintId))
            {
                EditorUtility.DisplayDialog("VRCW Hotswap",
                "This scene needs a world ID.\n\nClick Build & Publish in the VRChat SDK first.",
                "Ok");
                return;
            }

            if (!TryGetBuilderWorldData(builder, out VRCWorld world, out string thumbnailPath, out string reflectFail))
            {
                ShowSdkApiMovedDialog(reflectFail);
                return;
            }

            if (string.IsNullOrWhiteSpace(world.Name))
            {
                EditorUtility.DisplayDialog("VRCW Hotswap",
                "Set a world name in the VRChat SDK Builder first.",
                "Ok");
                return;
            }

            bool creatingNew = string.IsNullOrWhiteSpace(world.ID);
            if (creatingNew && (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)))
            {
                EditorUtility.DisplayDialog("VRCW Hotswap",
                "New worlds need a thumbnail.\n\nSet one in the VRChat SDK Builder, then try again.",
                "Ok");
                return;
            }

            if (!TryValidateHotswapIntegrity(lastBuild, clearSessionOnMismatch: true))
            return;

            long fileBytes = new FileInfo(lastBuild).Length;
            string sizeLabel = FormatByteSize(fileBytes);
            if (fileBytes > WorldUploadMaxBytes)
            {
                string tryLabel = fileBytes > WorldUploadHopelessBytes
                ? "I understand it will likely fail"
                : "Try anyway";
                if (!EditorUtility.DisplayDialog(
                "VRCW Hotswap",
                BuildOversizeUploadMessage(fileBytes, sizeLabel),
                tryLabel,
                "Ok"))
                {
                    return;
                }
            }

            if (IsAndroidBuildTarget)
            {
                if (!EditorUtility.DisplayDialog(
                "VRCW Hotswap - Android",
                AndroidUploadDisclaimer + "\n\nContinue?",
                "Continue",
                "Cancel"))
                {
                    return;
                }
            }

            if (!EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "Upload now?\n\n" +
            $"Platform: {UploadPlatformLabel}\n" +
            $"Size: {sizeLabel}\n" +
            $"Name: {world.Name}\n" +
            $"World ID: {pipeline.blueprintId}\n" +
            $"{(creatingNew ? "Creates a new world." : "Updates your existing world.")}\n\n" +
            "Tip: after this, don't click Build & Publish.",
            "Upload",
            "Cancel"))
            {
                return;
            }

            uploadProgressStatus = "Starting upload...";
            uploadProgress01 = 0f;
            cancelRequested = false;
            activeUploadBuilder = builder;
            uploadCts = new CancellationTokenSource();

            uploadStartHandler = (sender, args) =>
            {
                uploadProgressStatus = "Uploading...";
                uploadProgress01 = Mathf.Max(uploadProgress01, 0.02f);
            };
            progressHandler = (sender, progress) =>
            {

                float p = progress.percentage;
                if (float.IsNaN(p) || float.IsInfinity(p)) p = 0f;
                if (p > 1f) p = p / 100f;
                uploadProgress01 = Mathf.Clamp01(p);
                if (!string.IsNullOrEmpty(progress.status))
                uploadProgressStatus = progress.status;
            };

            builder.OnSdkUploadStart += uploadStartHandler;
            builder.OnSdkUploadProgress += progressHandler;
            EditorApplication.update += UploadProgressTick;

            EditorUtility.DisplayCancelableProgressBar(
            "VRCW Hotswap",
            $"{uploadProgressStatus} ({sizeLabel})",
            uploadProgress01);

            Debug.Log($"<color=cyan>VRCW Hotswap:</color> uploading ({sizeLabel})...\n");
            await builder.UploadLastBuild(world, thumbnailPath, uploadCts.Token);

            EditorApplication.Beep();
            if (cancelRequested || uploadCts.IsCancellationRequested)
            {
                if (!ConsumeSuppressUploadResultDialog())
                EditorUtility.DisplayDialog("VRCW Hotswap", "Upload cancelled.", "Ok");
            }
            else if (!ConsumeSuppressUploadResultDialog())
            {
                EditorUtility.DisplayDialog(
                "VRCW Hotswap",
                "Upload finished.\n\nCheck the VRChat SDK panel if anything looks wrong.",
                "Ok");
            }
        }
        catch (OperationCanceledException)
        {
            EditorApplication.Beep();
            if (!ConsumeSuppressUploadResultDialog())
            EditorUtility.DisplayDialog("VRCW Hotswap", "Upload cancelled.", "Ok");
            Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> upload cancelled.\n");
        }
        catch (Exception e)
        {
            Debug.LogError("Upload failed:\n" + e + "\n");
            EditorApplication.Beep();
            if (cancelRequested || (uploadCts != null && uploadCts.IsCancellationRequested))
            {
                if (!ConsumeSuppressUploadResultDialog())
                EditorUtility.DisplayDialog("VRCW Hotswap", "Upload cancelled.", "Ok");
                return;
            }
            if (ConsumeSuppressUploadResultDialog())
            return;
            if (LooksLikeSdkApiBreak(e))
            {
                ShowSdkApiMovedDialog(e.GetType().Name + ": " + e.Message);
                return;
            }
            EditorUtility.DisplayDialog("VRCW Hotswap", "Upload failed:\n" + e.Message + "\n\nSee the Console for more info.", "Ok");
        }
        finally
        {
            EditorApplication.update -= UploadProgressTick;
            if (builder != null)
            {
                if (uploadStartHandler != null)
                builder.OnSdkUploadStart -= uploadStartHandler;
                if (progressHandler != null)
                builder.OnSdkUploadProgress -= progressHandler;
            }
            activeUploadBuilder = null;
            try { uploadCts?.Dispose(); } catch { }
            uploadCts = null;
            suppressUploadResultDialog = false;
            EndOperation();
        }
    }

    private static bool ConsumeSuppressUploadResultDialog()
    {
        if (!suppressUploadResultDialog) return false;
        suppressUploadResultDialog = false;
        return true;
    }

    private static void UploadProgressTick()
    {
        string info = string.IsNullOrEmpty(uploadProgressStatus) ? "Uploading..." : uploadProgressStatus;
        info += $" ({Mathf.RoundToInt(uploadProgress01 * 100f)}%)";

        if (UpdateCancelableProgress("VRCW Hotswap", info, uploadProgress01))
        {
            Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> upload cancel requested...\n");
            try { activeUploadBuilder?.CancelUpload(); } catch { }
            try { uploadCts?.Cancel(); } catch { }
        }
    }

    private static bool LooksLikeSdkApiBreak(Exception e)
    {
        for (Exception cur = e; cur != null; cur = cur.InnerException)
        {
            if (cur is MissingMethodException || cur is MissingFieldException || cur is TypeLoadException)
            return true;
            string msg = cur.Message ?? "";
            if (msg.IndexOf("UploadLastBuild", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("no overload", StringComparison.OrdinalIgnoreCase) >= 0))
            return true;
        }
        return false;
    }

    private static void ShowSdkApiMovedDialog(string detail)
    {
        string sdkHint = TryGetInstalledWorldsSdkVersionHint();
        string body =
        "Can't talk to the VRChat SDK the way this tool expects.\n\n" +
        "Often this means the Worlds SDK updated and broke this tool.\n\n" +
        $"Tested with:\n" +
        $"• SDK {TestedWorldsSdkVersion2} / Unity {TestedUnityVersion2}\n" +
        $"• SDK {TestedWorldsSdkVersion} / Unity {TestedUnityVersion}\n" +
        $"Your Unity: {Application.unityVersion}\n" +
        $"Your SDK (guess): {sdkHint}\n\n" +
        "Try: open VRChat SDK > Builder, sign in, fill name / image, then retry.\n" +
        "If you just updated the SDK, note your versions and check the Console.\n\n" +
        "Detail:\n" + (string.IsNullOrEmpty(detail) ? "(none)" : detail);

        Debug.LogError("<color=cyan>VRCW Hotswap:</color> SDK mismatch.\n" + detail + "\n");
        EditorUtility.DisplayDialog("VRCW Hotswap - SDK problem?", body, "Ok");
    }

    private static string TryGetInstalledWorldsSdkVersionHint()
    {
        try
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VRCSceneDescriptor).Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.version))
            return pkg.version + " (" + pkg.name + ")";
        }
        catch { }

        try
        {
            var asm = typeof(VRCSceneDescriptor).Assembly;
            var name = asm.GetName();
            if (name != null && name.Version != null && name.Version.ToString() != "0.0.0.0")
            return name.Version.ToString() + " (assembly)";
        }
        catch { }

        return "unknown (check Packages / Creator Companion)";
    }

    private static bool TryGetBuilderWorldData(
    IVRCSdkWorldBuilderApi builder,
    out VRCWorld world,
    out string thumbnailPath,
    out string failDetail)
    {
        world = default;
        thumbnailPath = null;
        failDetail = null;
        try
        {
            if (builder == null)
            {
                failDetail = "Builder API instance was null.";
                return false;
            }

            var type = builder.GetType();
            FieldInfo worldField =
            type.GetField("_worldData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
            type.GetField("worldData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (worldField == null)
            {
                failDetail =
                $"Missing expected field _worldData on {type.FullName}. " +
                "A Worlds SDK update likely renamed Builder internals.";
                return false;
            }

            object raw = worldField.GetValue(builder);
            if (!(raw is VRCWorld w))
            {
                failDetail =
                $"Field {worldField.Name} on {type.Name} was " +
                (raw == null ? "null" : raw.GetType().FullName) +
                " (expected VRCWorld). Fill the Builder panel, or the SDK field layout may have changed.";
                return false;
            }

            world = w;

            FieldInfo thumbField =
            type.GetField("_newThumbnailImagePath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
            type.GetField("newThumbnailImagePath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (thumbField != null)
            thumbnailPath = thumbField.GetValue(builder) as string;

            return true;
        }
        catch (Exception e)
        {
            failDetail = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }
#endif

    [MenuItem("VRCW Hotswap/Inspect World File", true)]
    private static bool ValidateInspectVrcw()
    {
        return !operationBusy && !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("VRCW Hotswap/Inspect World File", false, 3)]
    public static void InspectVrcw()
    {
        if (!TryBeginOperation("Inspect"))
        return;

        string vrcwPath = EditorUtility.OpenFilePanelWithFilters(
        "Select a .vrcw to inspect",
        Application.dataPath,
        new[] { "World Files", "vrcw", "All files", "*" });
        if (string.IsNullOrEmpty(vrcwPath))
        {
            EndOperation();
            return;
        }

        string tmp = ProjTempPath + "/inspect_world.vrcw";
        try { File.Delete(tmp); } catch { }
        PrepareUncompressedVrcw(
        vrcwPath,
        tmp,
        "Inspect World File",
        "Reading...",
        AbroProgressInspect,
        readyPath =>
        {
            try
            {
                if (cancelRequested)
                {
                    if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(tmp); } catch { }
                    ConsumeCancelIfRequested("Inspect cancelled.", mentionUpload: false);
                    return;
                }

                var scan = ScanDecompressedVrcw(readyPath);
                if (scan == null)
                {
                    if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(tmp); } catch { }
                    if (ConsumeCancelIfRequested("Inspect cancelled.", mentionUpload: false))
                    return;
                    FailOperation(
                    "Couldn't read that .vrcw.\n\nCheck the Console, then try again.",
                    "Inspect failed: scan returned null.\n");
                    return;
                }

                var guess = GuessBundlePlatform(vrcwPath);
                string genVer = null;
                int fsFormat = -1;
                string genDetail = null;
                bool genOk = TryReadUnityFsGeneratorVersion(vrcwPath, out genVer, out fsFormat, out genDetail);
                var sb = new StringBuilder();
                sb.AppendLine($"File: {vrcwPath}");
                sb.AppendLine($"Size: {FormatByteSize(new FileInfo(vrcwPath).Length)}");
                if (genOk)
                {
                    sb.AppendLine($"Built with Unity: {genVer}");
                    sb.AppendLine($"Bundle format: {fsFormat}");
                    sb.AppendLine(
                    IsSupportedHotswapUnityVersion(genVer)
                    ? $"Version check: OK ({DescribeAcceptedUnityVersion(genVer)})"
                    : $"Version check: WRONG (want {TestedUnityVersion}, or match your Editor {Application.unityVersion})");
                }
                else
                sb.AppendLine($"Built with Unity: (couldn't read: {genDetail ?? "n/a"})");
                if (lastCompressionProbeResult == true)
                sb.AppendLine($"Compression: already uncompressed ({lastCompressionProbeDetail})");
                else if (lastCompressionProbeResult == false)
                sb.AppendLine($"Compression: compressed ({lastCompressionProbeDetail})");
                else
                sb.AppendLine($"Compression: unknown ({lastCompressionProbeDetail ?? "n/a"})");
                sb.AppendLine($"Platform guess: {guess}");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(scan.PipelineBlueprintId))
                sb.AppendLine($"Main world ID: {scan.PipelineBlueprintId}");
                else
                sb.AppendLine("Main world ID: (not found)");
                sb.AppendLine();
                sb.AppendLine($"World IDs found: {scan.WorldIds.Count}");
                foreach (var id in scan.WorldIds)
                sb.AppendLine($" {id} (x{scan.WorldIdCounts[id]})");
                sb.AppendLine();
                sb.AppendLine($"Scene names: {scan.BuildPlayerNames.Count}");
                foreach (var n in scan.BuildPlayerNames)
                sb.AppendLine($" {n}");
                sb.AppendLine();
                sb.AppendLine($"Other Unity versions in file: {string.Join(", ", scan.UnityVersions)}");
                sb.AppendLine();
                sb.AppendLine("Extra world IDs are usually portal links.");

                Debug.Log($"<color=cyan>World file inspect</color>\n{sb}");
                EditorUtility.DisplayDialog("Inspect World File", TruncateForDialog(sb.ToString()), "Ok");
                if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(tmp); } catch { }
                EndOperation();
            }
            catch (Exception e)
            {
                if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(tmp); } catch { }
                FailOperation("Inspect failed:\n" + e.Message, "Inspect failed:\n" + e + "\n");
            }
        },
        onFailed: EndOperation);
    }

    private static string TruncateForDialog(string text, int max = 1500)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + "\n\n...(see Console for full list)";
    }

    [MenuItem("VRCW Hotswap/Spawn Dummy World", true)]
    private static bool ValidateSpawnDummy()
    {
#if VRC_SDK_VRCSDK3
        return !EditorApplication.isPlayingOrWillChangePlaymode;
#else
        return false;
#endif
    }

    [MenuItem("VRCW Hotswap/Spawn Dummy World", false, 4)]
    public static void SpawnDummyWorld()
    {
#if !VRC_SDK_VRCSDK3
        EditorUtility.DisplayDialog("VRCW Hotswap", "VRChat World SDK is not in this project.", "Ok");
        return;
#else
        var existing = FindFirstSceneObject<VRCSceneDescriptor>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("<color=cyan>This scene already has a VRCWorld setup.</color>\n");
            EditorUtility.DisplayDialog("VRCW Hotswap",
            "This scene already has a world setup.\nSelected it.",
            "Ok");
            return;
        }

        GameObject go = null;
        string[] guids = AssetDatabase.FindAssets("VRCWorld t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || path.EndsWith(".meta")) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<VRCSceneDescriptor>() == null) continue;
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Dummy World";
            break;
        }

        if (go == null)
        {
            go = new GameObject("Dummy World");
            var descriptor = go.AddComponent<VRCSceneDescriptor>();
            go.AddComponent<PipelineManager>();
            var spawn = new GameObject("Spawn");
            spawn.transform.SetParent(go.transform, false);
            descriptor.spawns = new[] { spawn.transform };
            Debug.LogWarning("Couldn't find the usual VRCWorld prefab; made a basic setup. You may need to fix spawns before Building.\n");
        }

        if (go.GetComponent<PipelineManager>() == null)
        go.AddComponent<PipelineManager>();

        var pm = go.GetComponent<PipelineManager>();
        if (pm != null && !string.IsNullOrWhiteSpace(pm.blueprintId))
        {
            Undo.RecordObject(pm, "Clear Dummy World Blueprint ID");
            pm.blueprintId = "";
            EditorUtility.SetDirty(pm);
        }

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("<color=cyan>Dummy world added.</color> Click Build & Publish in the VRChat SDK, then Load Hotswap File.\n");
#endif
    }

#if VRC_SDK_VRCSDK3
    private static PipelineManager FindScenePipelineManager()
    {
        var descriptors = FindSceneObjects<VRCSceneDescriptor>();
        foreach (var d in descriptors)
        {
            var pm = d.GetComponent<PipelineManager>();
            if (pm != null) return pm;
        }
        return FindFirstSceneObject<PipelineManager>();
    }
#endif

    private static T FindFirstSceneObject<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>();
    }

    private static T[] FindSceneObjects<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);
    }

    private static void AnalyzeAndRewrite()
    {
        string sourcePath = string.IsNullOrEmpty(activeUncompressedRecoveredPath)
        ? DecompRecoveredPath
        : activeUncompressedRecoveredPath;

        var recovered = ScanDecompressedVrcw(sourcePath);
        if (recovered == null)
        {
            ConsumeCancelIfRequested("Hotswap cancelled while scanning.");
            return;
        }

        if (recovered.WorldIds.Count == 0)
        {
            FailOperation(
            "No world IDs found in that .vrcw.\n\n" +
            "This file may not be a valid world bundle.",
            "No world IDs found in that .vrcw.\n");
            return;
        }

        string oldWorldId = ChooseOldBlueprintId(recovered);
        if (string.IsNullOrEmpty(oldWorldId))
        {
            Debug.LogWarning("VRCW Hotswap cancelled (no world ID selected).\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog("VRCW Hotswap", "Cancelled.\n\nNo world ID was selected.", "Ok");
            EndOperation();
            return;
        }

        if (oldWorldId == pendingNewWorldId)
        {
            EditorUtility.DisplayDialog("VRCW Hotswap",
            "This file already uses the same world ID as your scene.\nNothing to change for the ID.",
            "Ok");
        }

        if (oldWorldId.Length != pendingNewWorldId.Length)
        {
            FailOperation(
            "Can't swap these world IDs.\n\n" +
            $"File ID length: {oldWorldId.Length}\n" +
            $"Scene ID length: {pendingNewWorldId.Length}\n\n" +
            "They must be the same length.",
            $"World ID length mismatch ({oldWorldId.Length} vs {pendingNewWorldId.Length}). Can't swap.\n");
            return;
        }

        var changes = new List<(string, string)>();
        if (oldWorldId != pendingNewWorldId)
        changes.Add((oldWorldId, pendingNewWorldId));

        if (changes.Count == 0)
        Debug.LogWarning("Nothing to rewrite; packing the file as-is.\n");
        else
        Debug.Log($"Hotswap changes ({changes.Count}):\n" +
        string.Join("\n", changes.Select(c => $" {c.Item1} -> {c.Item2}")) + "\n");

        try
        {
            if (!CreateModifiedFile(sourcePath, DecompModPath, changes))
            {
                ConsumeCancelIfRequested("Hotswap cancelled while updating IDs.");
                return;
            }
        }
        catch (Exception e)
        {
            FailOperation(
            "Failed while updating world IDs:\n" + e.Message + "\n\nSee the Console for more info.",
            "Failed while updating IDs:\n" + e.Message + "\n");
            return;
        }

        if (cancelRequested)
        {
            ConsumeCancelIfRequested("Hotswap cancelled.");
            return;
        }

        CompressAndFinalize();
    }

    private static string ChooseOldBlueprintId(VrcwScan scan)
    {
        if (!string.IsNullOrEmpty(scan.PipelineBlueprintId))
        {
            Debug.Log($"Using world ID next to blueprintId:\n{scan.PipelineBlueprintId}\n");
            return scan.PipelineBlueprintId;
        }

        if (scan.WorldIds.Count == 1)
        return scan.WorldIds[0];

        int pick = EditorUtility.DisplayDialogComplex(
        "Multiple world IDs found",
        $"This file has {scan.WorldIds.Count} world IDs.\n" +
        "Extras are usually portal links.\n\n" +
        "Pick the main world ID?",
        "Pick ID",
        "Cancel",
        "Use First");

        if (pick == 1) return null;
        if (pick == 2) return scan.WorldIds[0];

        return VRCWorldHotswapIdPicker.ShowModal(scan.WorldIds, scan.WorldIdCounts);
    }

    private static void CompressAndFinalize()
    {
        File.Delete(TmpOutPath);
        int op = BeginAsyncOp();
        EditorUtility.DisplayCancelableProgressBar("VRCW Hotswap", "Packing...", 0f);

        abro = AssetBundle.RecompressAssetBundleAsync(DecompModPath, TmpOutPath, BuildCompression.LZ4Runtime);
        EditorApplication.update += AbroProgressCompress;
        abro.completed += _ =>
        {
            EditorApplication.update -= AbroProgressCompress;
            EditorUtility.ClearProgressBar();

            bool unityOk = abro != null && abro.success && File.Exists(TmpOutPath) && new FileInfo(TmpOutPath).Length > 64;
            string unityResult = abro != null ? abro.result.ToString() : "null";
            abro = null;

            if (!IsCurrentAsyncOp(op))
            {
                File.Delete(DecompRecoveredPath);
                File.Delete(DecompModPath);
                File.Delete(TmpOutPath);
                return;
            }

            if (ConsumeCancelIfRequested("Hotswap cancelled during packing."))
            return;

            if (unityOk)
            {
                AfterCompress();
                return;
            }

            Debug.LogError(
            $"VRCW Hotswap failed: Unity packing failed ({unityResult}).\n" +
            "The modified file could not be recompressed. Try again, or use a different .vrcw.\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "Packing failed.\n\n" +
            $"Result: {unityResult}\n\n" +
            "Nothing was uploaded. Check the Console, then try again.",
            "Ok");
            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);
            File.Delete(TmpOutPath);
            EndOperation();
        };
    }

    private static void AfterCompress()
    {
        EditorUtility.ClearProgressBar();

        if (!File.Exists(TmpOutPath) || new FileInfo(TmpOutPath).Length < 64)
        {
            long badBytes = File.Exists(TmpOutPath) ? new FileInfo(TmpOutPath).Length : 0;
            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);
            File.Delete(TmpOutPath);
            FailOperation(
            "Packing produced an empty or invalid file.\n\nNothing was uploaded. Try again, or use a different .vrcw.",
            $"VRCW Hotswap failed: recompression produced empty/invalid output ({badBytes} bytes).\n");
            return;
        }

        int op = BeginAsyncOp();
        abcr = AssetBundle.LoadFromFileAsync(TmpOutPath);
        EditorUtility.DisplayCancelableProgressBar("VRCW Hotswap", "Checking result...", 0f);
        EditorApplication.update += AbcrProgress;
        abcr.completed += _ =>
        {
            EditorApplication.update -= AbcrProgress;
            EditorUtility.ClearProgressBar();
            var bundle = abcr != null ? abcr.assetBundle : null;
            abcr = null;

            if (!IsCurrentAsyncOp(op))
            {
                if (bundle != null) bundle.Unload(true);
                File.Delete(DecompRecoveredPath);
                File.Delete(DecompModPath);
                File.Delete(TmpOutPath);
                return;
            }

            if (ConsumeCancelIfRequested("Hotswap cancelled while checking result."))
            {
                if (bundle != null) bundle.Unload(true);
                return;
            }

            if (bundle == null)
            {
                File.Delete(DecompRecoveredPath);
                File.Delete(DecompModPath);
                File.Delete(TmpOutPath);
                FailOperation(
                "The packed file couldn't be opened.\n\nNothing was uploaded. Try again, or use a different .vrcw.",
                "VRCW Hotswap failed: the packed file couldn't be opened.\n");
                return;
            }

            string[] scenes = bundle.GetAllScenePaths();
            bool ok = (scenes != null && scenes.Length > 0) || bundle.isStreamedSceneAssetBundle;
            if (!ok)
            {
                try { ok = bundle.GetAllAssetNames().Length > 0; }
                catch { }
            }
            bundle.Unload(true);

            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);

            if (!ok)
            {
                File.Delete(TmpOutPath);
                FailOperation(
                "The packed file has no scene/assets.\n\nNothing was uploaded. Try a different .vrcw.",
                "VRCW Hotswap failed: no scene/assets found in output bundle.\n");
                return;
            }

            WriteFinalOutput();
        };
    }

    private static void WriteFinalOutput()
    {
        string dest = pendingOutputPath;
        if (string.IsNullOrEmpty(dest))
        {
            dest = Path.Combine(
            Path.GetDirectoryName(pendingRecoveredPath) ?? Application.dataPath,
            Path.GetFileNameWithoutExtension(pendingRecoveredPath) + "_hotswapped.vrcw");
            dest = EditorUtility.SaveFilePanel("Save hotswapped world", Path.GetDirectoryName(dest), Path.GetFileName(dest), "vrcw");
            if (string.IsNullOrEmpty(dest))
            {
                Debug.LogWarning("VRCW Hotswap cancelled at save dialog.\n");
                File.Delete(TmpOutPath);
                EndOperation();
                return;
            }
        }

        try
        {
            File.Copy(TmpOutPath, dest, true);
            RememberHotswapOutput(dest);
        }
        catch (Exception e)
        {
            FailOperation(
            "Couldn't write the hotswapped file:\n" + e.Message + "\n\nNothing was uploaded.",
            "Failed to write hotswapped VRCW:\n" + e.Message + "\n");
            return;
        }
        finally
        {
            File.Delete(TmpOutPath);
        }

        EditorApplication.Beep();
        long outBytes = File.Exists(dest) ? new FileInfo(dest).Length : 0;
        string sizeNote = outBytes > 0 ? $"Size: {FormatByteSize(outBytes)}\n\n" : "";
        string sizeWarning = outBytes > WorldUploadMaxBytes
        ? BuildOversizeHint(outBytes) + "\n\n"
        : "";
        string androidPackNote = BuildAndroidPackedSizeNote(outBytes);
        string androidNote = IsAndroidBuildTarget
        ? AndroidUploadDisclaimer + "\n\n"
        : "";
        EditorUtility.DisplayDialog(
        "VRCW Hotswap",
        "World loaded!\n\n" +
        "Your original .vrcw was not changed.\n\n" +
        sizeNote +
        androidPackNote +
        sizeWarning +
        androidNote +
        $"World ID: {pendingNewWorldId}\n\n" +
        "Next: VRCW Hotswap > Upload Hotswapped Build\n" +
        "Don't click Build & Publish after this.",
        "Ok");
        Debug.Log($"<color=cyan>HOTSWAP OK</color>\n{dest}\nID={pendingNewWorldId}\nSize={FormatByteSize(outBytes)}\nPlatform={UploadPlatformLabel}\n");
        EndOperation();
    }

    private static string BuildAndroidPackedSizeNote(long packedBytes)
    {
        if (!IsAndroidBuildTarget || packedBytes <= 0)
        return "";

        string packedLabel = FormatByteSize(packedBytes);
        string limitLabel = FormatByteSize(AndroidUploadMaxBytes);
        if (packedBytes <= AndroidUploadMaxBytes)
        return $"Android packed size: {packedLabel} (under {limitLabel} limit). OK to upload.\n\n";

        return $"Android packed size: {packedLabel} (over {limitLabel} limit). Upload will probably fail.\n\n";
    }

    private static string BuildOversizeUploadMessage(long fileBytes, string sizeLabel)
    {
        return $"This file looks too big for {UploadPlatformLabel}.\n\n" +
        $"Size: {sizeLabel}\n" +
        $"Usual limit: about {FormatByteSize(WorldUploadMaxBytes)}\n\n" +
        BuildOversizeHint(fileBytes);
    }

    private static string BuildOversizeHint(long fileBytes)
    {
        if (IsAndroidBuildTarget)
        {
            if (fileBytes > AndroidUploadHopelessBytes)
            return "Android worlds must be under 100 MB. This is over that.";

            return "Android worlds must stay under 100 MB packed.";
        }

        if (fileBytes > WorldUploadHopelessBytes)
        return "Over about 2.5 GB almost never works.";

        if (fileBytes > WorldUploadUnlikelyBytes)
        return "This size often fails with \"That file is much too big\".";

        return "It might still work. If not, VRChat will say the file is too big.";
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.#") + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.#") + " MB";
        return (mb / 1024.0).ToString("0.##") + " GB";
    }

    [MenuItem("VRCW Hotswap/Reset Current Hotswap", true)]
    private static bool ValidateResetCurrentHotswap()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
        (SessionState.GetBool(SessionHotswapActiveKey, false) || operationBusy);
    }

    [MenuItem("VRCW Hotswap/Reset Current Hotswap", false, 2200)]
    public static void ResetCurrentHotswap()
    {
        bool wasActive = SessionState.GetBool(SessionHotswapActiveKey, false);
        bool uploadInFlight = IsUploadInFlight();

        if (!wasActive && !operationBusy)
        {
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "Nothing to reset.\nNo hotswap is loaded right now.",
            "Ok");
            return;
        }

        string confirm =
        uploadInFlight
        ? "An upload is still running.\n\n" +
        "Cancel the upload and clear the current hotswap?\n\n" +
        "This only resets this tool. It does not delete worlds on VRChat."
        : operationBusy
        ? "Something is still running (load/inspect/pack).\n\n" +
        "Cancel it and clear the current hotswap?\n\n" +
        "This only resets this tool so you can load another file."
        : "Clear the current hotswap?\n\n" +
        "This only resets this tool so you can load another file.\n" +
        "Nothing is uploaded.";

        if (!EditorUtility.DisplayDialog(
        "VRCW Hotswap",
        confirm,
        uploadInFlight || operationBusy ? "Cancel & Reset" : "Reset",
        "Keep Going"))
        {
            return;
        }

        try
        {
            AbortAllWorkForReset();

            EditorApplication.Beep();
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            uploadInFlight
            ? "Upload cancel requested and hotswap cleared.\n\nYou can Load Hotswap File again."
            : "Cleared.\n\nYou can Load Hotswap File again.",
            "Ok");
            Debug.Log("<color=cyan>VRCW Hotswap reset.</color>\n");
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to reset hotswap:\n" + e.Message + "\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog("VRCW Hotswap", "Reset failed:\n" + e.Message, "Ok");
        }
    }

    private static bool IsUploadInFlight()
    {
#if VRC_SDK_VRCSDK3
        return uploadCts != null || activeUploadBuilder != null;
#else
        return uploadCts != null;
#endif
    }

    private static void AbortAllWorkForReset()
    {
        cancelRequested = true;
        asyncOpSerial++;

#if VRC_SDK_VRCSDK3
        try { activeUploadBuilder?.CancelUpload(); } catch { }
#endif
        try { uploadCts?.Cancel(); } catch { }
        try { EditorUtility.ClearProgressBar(); } catch { }

        bool uploadStillFinishing = IsUploadInFlight();

        ClearHotswapSessionFlags();
        CleanupTempFiles();
        try { File.Delete(ProjTempPath + "/inspect_world.vrcw"); } catch { }

        pendingRecoveredPath = null;
        pendingOutputPath = null;
        pendingNewWorldId = null;
        activeUncompressedRecoveredPath = null;
        abro = null;
        abcr = null;

        if (uploadStillFinishing)
        {

            operationBusy = true;
            suppressUploadResultDialog = true;
            Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> reset during upload; cancel requested.\n");
        }
        else
        {
            suppressUploadResultDialog = false;
            EndOperation();
        }
    }

    private static void ClearHotswapSessionFlags()
    {
        SessionState.SetBool(SessionHotswapActiveKey, false);
        SessionState.EraseString(SessionHotswapDestKey);
        SessionState.EraseString(SessionHotswapSizeKey);
        SessionState.EraseString(SessionHotswapFpKey);
    }

    private static void ClearPriorHotswapForReload()
    {
        cancelRequested = false;
        asyncOpSerial++;
        ClearHotswapSessionFlags();
        CleanupTempFiles();
        pendingRecoveredPath = null;
        pendingOutputPath = null;
        pendingNewWorldId = null;
        activeUncompressedRecoveredPath = null;
        abro = null;
        abcr = null;
    }

    private static void RememberHotswapOutput(string dest)
    {
        SessionState.SetString(SessionLastBuildPathKey, dest);
        SessionState.SetBool(SessionHotswapActiveKey, true);
        SessionState.SetString(SessionHotswapDestKey, dest);
        long size = new FileInfo(dest).Length;
        SessionState.SetString(SessionHotswapSizeKey, size.ToString());
        SessionState.SetString(SessionHotswapFpKey, ComputeQuickFileFingerprint(dest));
    }

    private static bool HasActiveHotswapReady()
    {
        if (!SessionState.GetBool(SessionHotswapActiveKey, false))
        return false;

        string dest = SessionState.GetString(SessionHotswapDestKey, null);
        if (string.IsNullOrEmpty(dest))
        dest = SessionState.GetString(SessionLastBuildPathKey, null);

        if (string.IsNullOrEmpty(dest) || !File.Exists(dest))
        return false;

        string expectedSize = SessionState.GetString(SessionHotswapSizeKey, null);
        if (!string.IsNullOrEmpty(expectedSize) &&
        long.TryParse(expectedSize, out long size) &&
        new FileInfo(dest).Length != size)
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateHotswapIntegrity(string path, bool clearSessionOnMismatch)
    {
        string expectedSize = SessionState.GetString(SessionHotswapSizeKey, null);
        string expectedFp = SessionState.GetString(SessionHotswapFpKey, null);
        if (string.IsNullOrEmpty(expectedSize) || string.IsNullOrEmpty(expectedFp))
        {

            return true;
        }

        if (!File.Exists(path))
        {
            if (clearSessionOnMismatch) ClearHotswapSessionFlags();
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "The hotswapped file is missing.\n\nLoad Hotswap File again.",
            "Ok");
            return false;
        }

        long size = new FileInfo(path).Length;
        if (!long.TryParse(expectedSize, out long wantSize) || size != wantSize)
        {
            if (clearSessionOnMismatch) ClearHotswapSessionFlags();
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "The hotswapped file changed.\n\n" +
            "Build & Publish probably overwrote it.\n\n" +
            "Load Hotswap File again.",
            "Ok");
            return false;
        }

        string fp = ComputeQuickFileFingerprint(path);
        if (!string.Equals(fp, expectedFp, StringComparison.Ordinal))
        {
            if (clearSessionOnMismatch) ClearHotswapSessionFlags();
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            "The hotswapped file changed.\n\n" +
            "Build & Publish probably overwrote it.\n\n" +
            "Load Hotswap File again.",
            "Ok");
            return false;
        }

        return true;
    }

    private static string ComputeQuickFileFingerprint(string path)
    {
        const int sample = 256 * 1024;
        long len = new FileInfo(path).Length;
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
        using (var ms = new MemoryStream(sample * 2 + 16))
        {
            byte[] lenBytes = BitConverter.GetBytes(len);
            ms.Write(lenBytes, 0, lenBytes.Length);

            byte[] buf = new byte[sample];
            int head = fs.Read(buf, 0, (int)Math.Min(sample, len));
            if (head > 0) ms.Write(buf, 0, head);

            if (len > sample * 2)
            {
                fs.Position = len - sample;
                int tail = fs.Read(buf, 0, sample);
                if (tail > 0) ms.Write(buf, 0, tail);
            }
            else if (len > head)
            {
                int rest = (int)(len - head);
                if (rest > buf.Length) buf = new byte[rest];
                int n = fs.Read(buf, 0, rest);
                if (n > 0) ms.Write(buf, 0, n);
            }

            byte[] hash = sha.ComputeHash(ms.ToArray());
            return len.ToString("x") + ":" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static bool TryBeginOperation(string label)
    {
        if (operationBusy)
        {
            EditorUtility.DisplayDialog(
            "VRCW Hotswap",
            $"Already busy.\n\nWait for it to finish, or use Reset Current Hotswap, then try {label} again.",
            "Ok");
            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("VRCW Hotswap", "Exit Play Mode first.", "Ok");
            return false;
        }

        cancelRequested = false;
        operationBusy = true;
        return true;
    }

    private static void EndOperation()
    {
        operationBusy = false;
        cancelRequested = false;
        try { EditorUtility.ClearProgressBar(); } catch { }
    }

    private static void FailOperation(string dialogMessage, string logMessage = null)
    {
        if (!string.IsNullOrEmpty(logMessage))
        Debug.LogError(logMessage);
        else
        Debug.LogError(dialogMessage + "\n");
        EditorApplication.Beep();
        try { EditorUtility.ClearProgressBar(); } catch { }
        EditorUtility.DisplayDialog("VRCW Hotswap", dialogMessage, "Ok");
        EndOperation();
    }

    private static int BeginAsyncOp()
    {
        asyncOpSerial++;
        return asyncOpSerial;
    }

    private static bool IsCurrentAsyncOp(int op) => op == asyncOpSerial;

    private static bool ConsumeCancelIfRequested(string message, bool mentionUpload = true)
    {
        if (!cancelRequested) return false;
        CleanupTempFiles();
        EditorApplication.Beep();
        string body = mentionUpload ? message + "\n\nNothing was uploaded." : message;
        EditorUtility.DisplayDialog("VRCW Hotswap", body, "Ok");
        EndOperation();
        return true;
    }

    private static bool UpdateCancelableProgress(string title, string info, float progress)
    {
        if (cancelRequested) return true;
        if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
        {
            cancelRequested = true;
            EditorUtility.ClearProgressBar();
            Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> cancel requested; finishing the current Unity step, then stopping.\n");
            return true;
        }
        return false;
    }

    private enum BundlePlatformGuess
    {
        Unknown,
        Pc,
        Android,
        Ambiguous
    }

    private static bool TryReadUnityFsGeneratorVersion(
    string path,
    out string generatorVersion,
    out int format,
    out string detail)
    {
        generatorVersion = null;
        format = -1;
        detail = null;
        try
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true))
            {
                byte[] sigBytes = br.ReadBytes(8);
                if (sigBytes.Length < 8)
                {
                    detail = "file too small";
                    return false;
                }

                string sig = Encoding.ASCII.GetString(sigBytes).TrimEnd('\0');
                if (sig != "UnityFS")
                {
                    detail = "not UnityFS (" + sig + ")";
                    return false;
                }

                format = (int)ReadUInt32BE(br);
                ReadNullTerminatedAscii(br);
                generatorVersion = ReadNullTerminatedAscii(br);
                if (string.IsNullOrEmpty(generatorVersion))
                {
                    detail = "empty generator version";
                    return false;
                }

                return true;
            }
        }
        catch (Exception e)
        {
            detail = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private static bool IsSupportedHotswapUnityVersion(string generatorVersion)
    {
        if (string.IsNullOrEmpty(generatorVersion))
        return false;

        if (string.Equals(generatorVersion, TestedUnityVersion, StringComparison.OrdinalIgnoreCase))
        return true;

        return string.Equals(generatorVersion, Application.unityVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeAcceptedUnityVersion(string generatorVersion)
    {
        if (string.Equals(generatorVersion, TestedUnityVersion, StringComparison.OrdinalIgnoreCase))
        return $"matches preferred {TestedUnityVersion}";

        if (string.Equals(generatorVersion, Application.unityVersion, StringComparison.OrdinalIgnoreCase))
        return $"matches this Editor ({Application.unityVersion})";

        return generatorVersion;
    }

    private static bool ConfirmUnityVersionOrContinue(string vrcwPath)
    {
        if (!TryReadUnityFsGeneratorVersion(vrcwPath, out string generatorVersion, out int _, out string detail))
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - Unity version?",
            "Couldn't read which Unity version made this file.\n\n" +
            $"Detail: {detail ?? "n/a"}\n\n" +
            "Best: use a .vrcw that matches this Editor, " +
            $"or a {TestedUnityVersion} .vrcw on Unity {TestedUnityVersion}.\n\n" +
            "Continue anyway?",
            "Continue Anyway",
            "Cancel");
        }

        if (IsSupportedHotswapUnityVersion(generatorVersion))
        {
            Debug.Log(
            $"<color=cyan>VRCW Hotswap:</color> Unity version OK " +
            $"({DescribeAcceptedUnityVersion(generatorVersion)}).\n");
            return true;
        }

        string editorVer = Application.unityVersion;
        string body =
        $"This file was built with:\n {generatorVersion}\n\n" +
        $"This Editor is:\n {editorVer}\n\n" +
        $"Important: open Unity {generatorVersion} and run hotswap there instead.\n\n" +
        $"Uploading a {generatorVersion} world from Editor {editorVer} is not recommended.\n" +
        "The upload might succeed, but joining the world usually will not work.\n\n" +
        $"Other options:\n" +
        $"- Use a .vrcw built with this Editor ({editorVer})\n" +
        $"- Or use a {TestedUnityVersion} .vrcw on Unity {TestedUnityVersion}\n\n" +
        "Continue anyway?";

        Debug.LogWarning(
        $"<color=cyan>VRCW Hotswap:</color> Unity mismatch: file={generatorVersion}, " +
        $"editor={editorVer}, preferred={TestedUnityVersion}.\n");

        return EditorUtility.DisplayDialog(
        "VRCW Hotswap - Unity version mismatch",
        body,
        "Continue Anyway",
        "Cancel");
    }

    private static bool ConfirmPlatformMatchOrContinue(string vrcwPath)
    {
        var guess = GuessBundlePlatform(vrcwPath);
        bool androidTarget = IsAndroidBuildTarget;
        string targetLabel = androidTarget ? "Android" : "PC";

        if (guess == BundlePlatformGuess.Unknown || guess == BundlePlatformGuess.Ambiguous)
        {
            string why = guess == BundlePlatformGuess.Ambiguous
            ? "This file has mixed PC and Android markers."
            : "Couldn't tell if this file is PC or Android.";

            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - Platform?",
            why + "\n\n" +
            $"Unity is currently set to: {targetLabel}\n\n" +
            "Make sure that matches your world, then continue.",
            "Continue",
            "Cancel");
        }

        if (androidTarget && guess == BundlePlatformGuess.Pc)
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - Wrong platform?",
            "Unity is set to Android, but this file looks like a PC world.\n\n" +
            "Continue anyway?",
            "Continue Anyway",
            "Cancel");
        }

        if (!androidTarget && guess == BundlePlatformGuess.Android)
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - Wrong platform?",
            "Unity is set to PC, but this file looks like an Android world.\n\n" +
            "Continue anyway?",
            "Continue Anyway",
            "Cancel");
        }

        return true;
    }

    private static bool ConfirmSourceSizeOrContinue(string vrcwPath)
    {
        return IsAndroidBuildTarget
        ? ConfirmAndroidSourceSizeOrContinue(vrcwPath)
        : ConfirmPcSourceSizeOrContinue(vrcwPath);
    }

    private static bool ConfirmPcSourceSizeOrContinue(string vrcwPath)
    {
        long bytes = new FileInfo(vrcwPath).Length;
        if (bytes <= PcUploadMaxBytes)
        return true;

        if (bytes <= PcUploadUnlikelyBytes)
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - PC size",
            "PC worlds over ~1 GB can get rejected.\n\n" +
            $"This file is {FormatByteSize(bytes)}.\n\n" +
            "Continue anyway?",
            "Continue Anyway",
            "Cancel");
        }

        if (bytes <= PcUploadHopelessBytes)
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - PC size",
            "PC worlds this large are often rejected (~1.5-2.5 GB).\n\n" +
            $"This file is {FormatByteSize(bytes)}.\n\n" +
            "Continue anyway?",
            "Continue Anyway",
            "Cancel");
        }

        return EditorUtility.DisplayDialog(
        "VRCW Hotswap - PC size",
        "PC worlds over ~2.5 GB almost never upload.\n\n" +
        $"This file is {FormatByteSize(bytes)}.\n\n" +
        "Continue anyway?",
        "I understand it will likely fail",
        "Cancel");
    }

    private static bool ConfirmAndroidSourceSizeOrContinue(string vrcwPath)
    {
        long bytes = new FileInfo(vrcwPath).Length;
        if (bytes <= AndroidPracticalSourceMaxBytes)
        return true;

        if (bytes <= AndroidTrySourceMaxBytes)
        {
            return EditorUtility.DisplayDialog(
            "VRCW Hotswap - Android size",
            "Android worlds must end up under 100 MB.\n\n" +
            $"This file is {FormatByteSize(bytes)}.\n" +
            "Packing might shrink it enough. Want to try?",
            "Try Packing",
            "Cancel");
        }

        return EditorUtility.DisplayDialog(
        "VRCW Hotswap - Android size",
        "Android worlds must end up under 100 MB.\n\n" +
        $"This file is {FormatByteSize(bytes)}.\n" +
        "Over 200 MB almost never works.\n\n" +
        "Continue anyway?",
        "I understand it will likely fail",
        "Cancel");
    }

    private static BundlePlatformGuess GuessBundlePlatform(string path)
    {

        string[] androidMarkers =
        {
            "Android", "ASTC", "astc", "ETC2", "OpenGLES3", "OpenGLES"
        };
        string[] pcMarkers =
        {
            "StandaloneWindows64", "StandaloneWindows", "Direct3D11", "Direct3D12",
            "D3D11", "D3D12", "DXT1", "DXT5", "BC7", "bc7", "WindowsPlayer"
        };

        int androidHits = 0;
        int pcHits = 0;
        try
        {
            const int maxRead = 8 * 1024 * 1024;
            using (var fs = File.OpenRead(path))
            {
                int toRead = (int)Math.Min(maxRead, fs.Length);
                byte[] buf = new byte[toRead];
                int n = fs.Read(buf, 0, toRead);
                string text = Latin1.GetString(buf, 0, n);
                foreach (string m in androidMarkers)
                if (text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) androidHits++;
                foreach (string m in pcMarkers)
                if (text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) pcHits++;
            }
        }
        catch
        {
            return BundlePlatformGuess.Unknown;
        }

        if (androidHits == 0 && pcHits == 0) return BundlePlatformGuess.Unknown;
        if (androidHits > 0 && pcHits > 0)
        {

            if (androidHits >= pcHits + 2) return BundlePlatformGuess.Android;
            if (pcHits >= androidHits + 2) return BundlePlatformGuess.Pc;
            return BundlePlatformGuess.Ambiguous;
        }
        if (androidHits > 0) return BundlePlatformGuess.Android;
        return BundlePlatformGuess.Pc;
    }

    private static void CleanupTempFiles()
    {
        try { File.Delete(DecompRecoveredPath); } catch { }
        try { File.Delete(DecompModPath); } catch { }
        try { File.Delete(TmpOutPath); } catch { }
    }

    private static void AbroProgressRecovered()
    {
        if (abro != null)
        UpdateCancelableProgress("VRCW Hotswap", "Preparing your world file...", abro.progress);
    }

    private static void AbroProgressCompress()
    {
        if (abro != null)
        UpdateCancelableProgress("VRCW Hotswap", "Packing...", abro.progress);
    }

    private static void AbroProgressInspect()
    {
        if (abro != null)
        UpdateCancelableProgress("Inspect World File", "Reading...", abro.progress);
    }

    private static void AbcrProgress()
    {
        if (abcr != null)
        UpdateCancelableProgress("VRCW Hotswap", "Checking result...", abcr.progress);
    }

    private static string lastCompressionProbeDetail;
    private static bool? lastCompressionProbeResult;

    private static void PrepareUncompressedVrcw(
    string vrcwPath,
    string tempUncompressedPath,
    string progressTitle,
    string progressInfo,
    EditorApplication.CallbackFunction progressCallback,
    Action<string> onReady,
    Action onFailed = null)
    {
        lastCompressionProbeResult = TryIsFullyUncompressedUnityFs(vrcwPath, out lastCompressionProbeDetail);
        if (lastCompressionProbeResult == true)
        {
            Debug.Log(
            $"<color=cyan>VRCW Hotswap:</color> already uncompressed ({lastCompressionProbeDetail}); skipping decompress.\n");
            onReady(vrcwPath);
            return;
        }

        Debug.Log(
        $"<color=cyan>VRCW Hotswap:</color> decompressing with Unity " +
        $"({lastCompressionProbeDetail ?? "compression unknown / not proven uncompressed"})...\n");

        File.Delete(tempUncompressedPath);
        int op = BeginAsyncOp();
        abro = AssetBundle.RecompressAssetBundleAsync(vrcwPath, tempUncompressedPath, BuildCompression.Uncompressed);
        EditorUtility.DisplayCancelableProgressBar(progressTitle, progressInfo, 0f);
        EditorApplication.update += progressCallback;
        abro.completed += _ =>
        {
            EditorApplication.update -= progressCallback;
            EditorUtility.ClearProgressBar();

            bool ok = abro != null && abro.success;
            string result = abro != null ? abro.result.ToString() : "";
            abro = null;

            if (!IsCurrentAsyncOp(op) || cancelRequested)
            {
                try { File.Delete(tempUncompressedPath); } catch { }
                if (cancelRequested)
                ConsumeCancelIfRequested("Cancelled while preparing the world file.");
                else
                onFailed?.Invoke();
                return;
            }

            if (!ok)
            {
                try { File.Delete(tempUncompressedPath); } catch { }
                FailOperation(
                "Could not open that .vrcw file.\n\n" +
                (string.IsNullOrEmpty(result) ? "Unknown error." : result) +
                "\n\nCheck the Console, then try again.",
                $"Could not open that .vrcw file.\n{result}\n");

                return;
            }

            onReady(tempUncompressedPath);
        };
    }

    private static bool? TryIsFullyUncompressedUnityFs(string path, out string detail)
    {
        detail = null;
        try
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true))
            {
                byte[] sigBytes = br.ReadBytes(8);
                if (sigBytes.Length < 8)
                {
                    detail = "file too small";
                    return null;
                }

                string sig = Encoding.ASCII.GetString(sigBytes).TrimEnd('\0');
                if (sig != "UnityFS")
                {
                    detail = "not UnityFS (" + sig + ")";
                    return null;
                }

                uint format = ReadUInt32BE(br);
                if (format < 6 || format > 8)
                {

                    if (format < 3)
                    {
                        detail = "unsupported UnityFS format " + format;
                        return null;
                    }
                }

                ReadNullTerminatedAscii(br);
                ReadNullTerminatedAscii(br);
                long headerFileSize = ReadInt64BE(br);
                uint compressedInfoSize = ReadUInt32BE(br);
                uint uncompressedInfoSize = ReadUInt32BE(br);
                uint flags = ReadUInt32BE(br);

                int infoCompType = (int)(flags & UnityFsCompressionTypeMask);
                bool infoAtEnd = (flags & UnityFsBlocksInfoAtTheEnd) != 0;
                bool needPadding = (flags & UnityFsBlockInfoNeedPaddingAtStart) != 0;

                if (compressedInfoSize == 0 || uncompressedInfoSize == 0 ||
                compressedInfoSize > int.MaxValue || uncompressedInfoSize > int.MaxValue)
                {
                    detail = "invalid blocks-info sizes";
                    return null;
                }

                if (infoCompType != 0)
                {
                    detail = "blocks-info compression type=" + infoCompType +
                    " (LZMA=1 LZ4=2 LZ4HC=3)";
                    return false;
                }

                if (compressedInfoSize != uncompressedInfoSize)
                {
                    detail = "blocks-info size mismatch for type=None";
                    return null;
                }

                long headerEnd = fs.Position;

                if ((format >= 7 && !infoAtEnd) || needPadding)
                {
                    long pad = (16 - (headerEnd % 16)) % 16;
                    fs.Position = headerEnd + pad;
                }

                byte[] info;
                if (infoAtEnd)
                {
                    if (compressedInfoSize > fs.Length)
                    {
                        detail = "blocks-info larger than file";
                        return null;
                    }
                    fs.Position = fs.Length - compressedInfoSize;
                    info = br.ReadBytes((int)compressedInfoSize);
                }
                else
                {
                    info = br.ReadBytes((int)compressedInfoSize);
                }

                if (info == null || info.Length != (int)compressedInfoSize)
                {
                    detail = "failed reading blocks-info";
                    return null;
                }

                int o = 16;
                if (o + 4 > info.Length)
                {
                    detail = "blocks-info too small";
                    return null;
                }

                uint blockCount = ReadUInt32BE(info, ref o);
                if (blockCount == 0 || blockCount > 5_000_000u)
                {
                    detail = "suspicious blockCount=" + blockCount;
                    return null;
                }

                long expectedMin = 16L + 4L + (long)blockCount * 10L + 4L;
                if (expectedMin > info.Length)
                {
                    detail = "blocks-info truncated for blockCount=" + blockCount;
                    return null;
                }

                int compressedBlocks = 0;
                for (uint i = 0; i < blockCount; i++)
                {
                    uint uSize = ReadUInt32BE(info, ref o);
                    uint cSize = ReadUInt32BE(info, ref o);
                    ushort blockFlags = ReadUInt16BE(info, ref o);
                    int blockComp = blockFlags & 0x3F;

                    if (blockComp != 0)
                    compressedBlocks++;
                    else if (cSize != uSize)
                    {
                        detail = "uncompressed block with mismatched sizes";
                        return null;
                    }
                }

                if (compressedBlocks > 0)
                {
                    detail = compressedBlocks + " compressed / " + blockCount + " blocks";
                    return false;
                }

                detail = "all " + blockCount + " blocks uncompressed" +
                (headerFileSize > 0 ? ", headerSize=" + headerFileSize : "");
                return true;
            }
        }
        catch (Exception e)
        {
            detail = e.GetType().Name + ": " + e.Message;
            return null;
        }
    }

    private static uint ReadUInt32BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static long ReadInt64BE(BinaryReader br)
    {
        byte[] b = br.ReadBytes(8);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToInt64(b, 0);
    }

    private static uint ReadUInt32BE(byte[] data, ref int offset)
    {
        uint v = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
        offset += 4;
        return v;
    }

    private static ushort ReadUInt16BE(byte[] data, ref int offset)
    {
        ushort v = (ushort)((data[offset] << 8) | data[offset + 1]);
        offset += 2;
        return v;
    }

    private static string ReadNullTerminatedAscii(BinaryReader br)
    {
        var sb = new StringBuilder(32);
        while (true)
        {
            int c = br.BaseStream.ReadByte();
            if (c <= 0) break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    private static VrcwScan ScanDecompressedVrcw(string path)
    {
        var scan = new VrcwScan();
        var worldRx = new Regex(WorldIdPattern, RegexOptions.Compiled);
        var bpRx = new Regex(BuildPlayerPattern, RegexOptions.Compiled);
        var unityRx = new Regex(UnityVerPattern, RegexOptions.Compiled);
        var blueprintHits = new Dictionary<string, int>();

        const string blueprintMarker = "blueprintId";
        const int blueprintLookAhead = 128;

        const int overlap = 256;
        const int chunkSize = 4 * 1024 * 1024;

        byte[] buf = new byte[chunkSize + overlap];
        int carry = 0;
        long fileLen = new FileInfo(path).Length;

        using (var fs = File.OpenRead(path))
        {
            while (true)
            {
                if (UpdateCancelableProgress("VRCW Hotswap", "Scanning world file...",
                fileLen > 0 ? Mathf.Clamp01((float)fs.Position / fileLen) : 0f))
                {
                    EditorUtility.ClearProgressBar();
                    return null;
                }

                int read = fs.Read(buf, carry, buf.Length - carry);
                int len = carry + read;
                if (len <= 0) break;

                bool eof = read == 0 || fs.Position >= fs.Length;

                int searchLen = eof ? len : Math.Max(0, len - overlap);
                if (searchLen > 0 || eof)
                {

                    int textLen = eof ? len : Math.Min(len, searchLen + overlap);
                    string text = Latin1.GetString(buf, 0, textLen);
                    int commitLen = eof ? text.Length : Math.Min(searchLen, text.Length);
                    AddScanMatches(text, commitLen, worldRx, bpRx, unityRx, scan);
                    AddBlueprintHits(text, commitLen, eof, worldRx, blueprintMarker, blueprintLookAhead, blueprintHits);
                }

                if (eof || read == 0) break;

                carry = len - searchLen;
                Buffer.BlockCopy(buf, searchLen, buf, 0, carry);
            }
        }

        if (blueprintHits.Count > 0)
        scan.PipelineBlueprintId = blueprintHits.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).First();

        EditorUtility.ClearProgressBar();
        return scan;
    }

    private static void AddScanMatches(
    string text,
    int commitLen,
    Regex worldRx,
    Regex bpRx,
    Regex unityRx,
    VrcwScan scan)
    {
        if (commitLen <= 0) return;

        foreach (Match m in worldRx.Matches(text))
        {
            if (m.Index >= commitLen) break;
            string id = m.Value;
            if (!scan.WorldIdCounts.ContainsKey(id))
            {
                scan.WorldIdCounts[id] = 0;
                scan.WorldIds.Add(id);
            }
            scan.WorldIdCounts[id]++;
        }

        foreach (Match m in bpRx.Matches(text))
        {
            if (m.Index >= commitLen) break;
            string name = Regex.Match(m.Value, @"BuildPlayer-[A-Za-z0-9 _\-]+").Value;
            if (!string.IsNullOrEmpty(name) && !scan.BuildPlayerNames.Contains(name))
            scan.BuildPlayerNames.Add(name);
        }

        foreach (Match m in unityRx.Matches(text))
        {
            if (m.Index >= commitLen) break;
            if (!scan.UnityVersions.Contains(m.Value))
            scan.UnityVersions.Add(m.Value);
        }
    }

    private static void AddBlueprintHits(
    string text,
    int commitLen,
    bool eof,
    Regex worldRx,
    string blueprintMarker,
    int blueprintLookAhead,
    Dictionary<string, int> blueprintHits)
    {
        if (commitLen <= 0) return;

        int idx = 0;
        while (idx < commitLen)
        {
            int at = text.IndexOf(blueprintMarker, idx, commitLen - idx, StringComparison.Ordinal);
            if (at < 0) break;

            int start = at + blueprintMarker.Length;
            int end = Math.Min(text.Length, start + blueprintLookAhead);

            if (!eof && end < start + Math.Min(blueprintLookAhead, 41))
            {
                idx = at + 1;
                continue;
            }

            if (end > start)
            {
                Match m = worldRx.Match(text, start, end - start);
                if (m.Success)
                {
                    if (!blueprintHits.ContainsKey(m.Value))
                    blueprintHits[m.Value] = 0;
                    blueprintHits[m.Value]++;
                }
            }
            idx = at + 1;
        }
    }

    private static bool CreateModifiedFile(string inputFile, string outputFile, List<(string, string)> stringsToReplace)
    {
        if (stringsToReplace == null || stringsToReplace.Count == 0)
        {
            File.Copy(inputFile, outputFile, true);
            return true;
        }

        foreach (var pair in stringsToReplace)
        {
            if (pair.Item1 == null || pair.Item2 == null || pair.Item1.Length != pair.Item2.Length)
            throw new InvalidOperationException("Hotswap replacements must be the same length.");
        }

        byte[][] fromBytes = stringsToReplace.Select(p => Latin1.GetBytes(p.Item1)).ToArray();
        byte[][] toBytes = stringsToReplace.Select(p => Latin1.GetBytes(p.Item2)).ToArray();
        int maxPat = fromBytes.Max(b => b.Length);
        const int chunkSize = 4 * 1024 * 1024;
        int overlap = Math.Max(0, maxPat - 1);

        long fileLen = new FileInfo(inputFile).Length;

        using (var input = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize))
        using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize))
        {
            byte[] buf = new byte[chunkSize + overlap];
            int carry = 0;

            while (true)
            {
                if (UpdateCancelableProgress("VRCW Hotswap", "Updating IDs...",
                fileLen > 0 ? Mathf.Clamp01((float)input.Position / fileLen) : 0f))
                {
                    EditorUtility.ClearProgressBar();
                    try { output.Close(); } catch { }
                    try { File.Delete(outputFile); } catch { }
                    return false;
                }

                int read = input.Read(buf, carry, buf.Length - carry);
                int len = carry + read;
                if (len <= 0) break;

                bool eof = read == 0 || input.Position >= input.Length;
                int processLen = eof ? len : Math.Max(0, len - overlap);

                ReplaceBytesInPlace(buf, processLen, fromBytes, toBytes);
                output.Write(buf, 0, processLen);

                if (eof || read == 0) break;

                carry = len - processLen;
                Buffer.BlockCopy(buf, processLen, buf, 0, carry);
            }
        }

        EditorUtility.ClearProgressBar();
        return true;
    }

    private static void ReplaceBytesInPlace(byte[] buf, int length, byte[][] fromBytes, byte[][] toBytes)
    {
        for (int i = 0; i < length;)
        {
            bool matched = false;
            for (int p = 0; p < fromBytes.Length; p++)
            {
                byte[] from = fromBytes[p];
                if (i + from.Length > length) continue;
                if (!BytesEqual(buf, i, from)) continue;

                Buffer.BlockCopy(toBytes[p], 0, buf, i, toBytes[p].Length);
                i += toBytes[p].Length;
                matched = true;
                break;
            }
            if (!matched) i++;
        }
    }

    private static bool BytesEqual(byte[] buf, int offset, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (buf[offset + i] != pattern[i]) return false;
        }
        return true;
    }

    private class VrcwScan
    {
        public List<string> WorldIds = new List<string>();
        public Dictionary<string, int> WorldIdCounts = new Dictionary<string, int>();
        public string PipelineBlueprintId;
        public List<string> BuildPlayerNames = new List<string>();
        public List<string> UnityVersions = new List<string>();
    }
}

public class VRCWorldHotswapIdPicker : EditorWindow
{
    private List<string> ids;
    private Dictionary<string, int> counts;
    private Vector2 scroll;
    private string selected;
    private bool confirmed;

    public static string ShowModal(List<string> ids, Dictionary<string, int> counts)
    {
        var window = CreateInstance<VRCWorldHotswapIdPicker>();
        window.titleContent = new GUIContent("Pick the main world ID");
        window.ids = ids;
        window.counts = counts;
        window.selected = ids[0];
        window.minSize = new Vector2(520, 320);
        window.ShowModalUtility();
        return window.confirmed ? window.selected : null;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
        "Pick this world's own ID.\nDon't pick portal links unless you mean to.",
        MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string id in ids)
        {
            int c = counts != null && counts.ContainsKey(id) ? counts[id] : 0;
            bool on = GUILayout.Toggle(selected == id, $"{id} (x{c})", "Button");
            if (on) selected = id;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel"))
        {
            confirmed = false;
            selected = null;
            Close();
        }
        if (GUILayout.Button("Use Selected"))
        {
            confirmed = true;
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

public class VRCWorldHotswapAboutWindow : EditorWindow
{
    private static readonly Color LinkColor = new Color(0.25f, 0.55f, 0.95f);
    private GUIStyle plainStyle;
    private GUIStyle linkStyle;

    public static void ShowWindow()
    {
        var window = GetWindow<VRCWorldHotswapAboutWindow>(true, "About VRCW Hotswap", true);
        window.minSize = new Vector2(460, 360);
        window.maxSize = new Vector2(560, 480);
        window.Show();
    }

    private void OnGUI()
    {
        if (plainStyle == null)
        {
            plainStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
        }

        if (linkStyle == null)
        {
            linkStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = LinkColor },
                hover = { textColor = new Color(0.35f, 0.65f, 1f) },
                active = { textColor = new Color(0.15f, 0.45f, 0.85f) }
            };
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("VRCW Hotswap", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Version " + VRCWorldHotswap.Version);
        EditorGUILayout.LabelField("Tested & working:", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
        $"• Worlds SDK {VRCWorldHotswap.TestedWorldsSdkVersion2} / Unity {VRCWorldHotswap.TestedUnityVersion2}",
        EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
        $"• Worlds SDK {VRCWorldHotswap.TestedWorldsSdkVersion} / Unity {VRCWorldHotswap.TestedUnityVersion}",
        EditorStyles.miniLabel);
        GUILayout.Space(6);
        EditorGUILayout.HelpBox(
        "Rewrites a recovered .vrcw to your world ID, swaps it onto the SDK's last build, and lets you upload it without rebuilding the scene.\n" +
            "Only use this on your own worlds.",
        MessageType.Info);

        if (GUILayout.Button("Show howto again", GUILayout.Height(26)))
        {
            VRCWorldHotswap.ResetHowtoPref();
            VRCWorldHotswap.ShowHowtoDialog();
        }

        GUILayout.Space(12);
        EditorGUILayout.LabelField("Credits", EditorStyles.boldLabel);
        DrawLinkedLine("Maintained by: ", VRCWorldHotswap.MaintainerName, null, VRCWorldHotswap.MaintainerUrl);
        DrawLinkedLine(
        "Based on ",
        VRCWorldHotswap.OriginalAuthorName,
        "'s Hotswap Script",
        VRCWorldHotswap.OriginalAuthorUrl);

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Needs the VRChat Worlds SDK.", EditorStyles.miniLabel);
        if (GUILayout.Button("Close", GUILayout.Height(28)))
        Close();
    }

    private void DrawLinkedLine(string prefix, string linkText, string suffix, string url)
    {
        EditorGUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(prefix))
        GUILayout.Label(prefix, plainStyle, GUILayout.ExpandWidth(false));

        GUIContent linkContent = new GUIContent(linkText);
        Vector2 linkSize = linkStyle.CalcSize(linkContent);
        Rect linkRect = GUILayoutUtility.GetRect(linkSize.x, linkSize.y, linkStyle, GUILayout.ExpandWidth(false));
        EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
        EditorGUI.LabelField(linkRect, linkContent, linkStyle);
        EditorGUI.DrawRect(new Rect(linkRect.x, linkRect.yMax - 1f, linkRect.width, 1f), LinkColor);

        if (Event.current.type == EventType.MouseDown &&
        Event.current.button == 0 &&
        linkRect.Contains(Event.current.mousePosition))
        {
            Application.OpenURL(url);
            Event.current.Use();
        }

        if (!string.IsNullOrEmpty(suffix))
        GUILayout.Label(suffix, plainStyle, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }
}
#endif
