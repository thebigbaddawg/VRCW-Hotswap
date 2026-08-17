#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    public const string Version = "1.0.4-beta";
    public const string TestedWorldsSdkVersion = "3.10.4";
    public const string TestedUnityVersion = "2022.3.22f1";
    public const string TestedUnityVersion2 = "2022.3.6f1";
    public const string TestedWorldsSdkVersion2 = "3.7.6";
    public const string TestedWorldsSdkVersion2019 = "3.4.2";
    public const string TestedUnityVersion2019 = "2019.4.31f1";
    public const string MaintainerName = "thebigbaddawg";
    public const string MaintainerUrl = "https://github.com/thebigbaddawg";
    public const string OriginalAuthorName = "FACS01";
    public const string OriginalAuthorUrl = "https://github.com/FACS01-01";

    public static string PreferredHotswapUnityVersion =>
#if UNITY_2019_4
        TestedUnityVersion2019;
#else
        TestedUnityVersion;
#endif

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
    private const string PrefsPackAdvancedModeKey = "VRCWorldHotswap.PackAdvancedMode";
    private const string PrefsPackFastModeKeyLegacy = "VRCWorldHotswap.PackFastMode";
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

    private static string T(string key) => VRCWorldHotswapLoc.T(key);
    private static string TF(string key, params object[] args) => VRCWorldHotswapLoc.TF(key, args);

    private static string UploadPlatformLabel =>
    IsAndroidBuildTarget ? T("platform.android") : T("platform.pc");

    private static string AndroidUploadDisclaimer =>
    T("android.disclaimer");

    private static AssetBundleRecompressOperation abro;
    private static AssetBundleCreateRequest abcr;
    private static Process packProcess;
    private static float packProgress01;
    private static bool packProcessExited;
    private static int packProcessExitCode;
    private static int packAsyncOp;
    private static string packCompressionLabel = "LZ4";
    private static DetectedBundleCompression detectedSourceCompression = DetectedBundleCompression.Unknown;
    private static long sourceFileBytes;
    private static string pendingRecoveredPath;
    private static string pendingOutputPath;
    private static string pendingNewWorldId;

    private static string activeUncompressedRecoveredPath;
    private static bool operationBusy;
    private static bool cancelRequested;

    private static bool suppressUploadResultDialog;
    private static int asyncOpSerial;
    private static string uploadProgressStatus = VRCWorldHotswapLoc.T("progress.uploading");
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
        TryKillPackProcess();
        try { uploadCts?.Cancel(); } catch { }
        try { uploadCts?.Dispose(); } catch { }
        uploadCts = null;
#if VRC_SDK_VRCSDK3
        activeUploadBuilder = null;
#endif
        try { EditorUtility.ClearProgressBar(); } catch { }
    }

    public static string HowtoDialogBody => T("howto.body");

    public static void ShowHowtoDialog()
    {
        VRCWorldHotswapLoc.PromptFirstRunLanguageIfNeeded();
        EditorUtility.DisplayDialog(T("app.name"), HowtoDialogBody, T("btn.ok"));
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
        EditorUtility.DisplayDialog(T("app.name"), T("dialog.sdk_missing"), T("btn.ok"));
        return;
#else
        if (!TryBeginOperation(T("action.hotswap")))
        return;

        if (SessionState.GetBool(SessionHotswapActiveKey, false))
        {
            if (!EditorUtility.DisplayDialog(
            T("app.name"),
            T("dialog.already_loaded"),
            T("btn.load_new_file"),
            T("btn.cancel")))
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
            T("app.name"),
            TF("dialog.howto_continue", HowtoDialogBody),
            T("btn.continue"),
            T("btn.cancel")))
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
            EditorUtility.DisplayDialog(T("app.name"),
            T("dialog.scene_no_world_setup"),
            T("btn.ok"));
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        if (string.IsNullOrWhiteSpace(pipeline.blueprintId))
        {
            EditorUtility.DisplayDialog(T("app.name"),
            T("dialog.scene_no_world_id"),
            T("btn.ok"));
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        pendingNewWorldId = pipeline.blueprintId.Trim();
        if (!Regex.IsMatch(pendingNewWorldId, "^" + WorldIdPattern + "$"))
        {
            EditorUtility.DisplayDialog(T("app.name"),
            TF("dialog.bad_world_id", pendingNewWorldId),
            T("btn.ok"));
            EditorApplication.Beep();
            EndOperation();
            return;
        }

        string lastBuild = SessionState.GetString(SessionLastBuildPathKey, null);
        if (string.IsNullOrEmpty(lastBuild) || !File.Exists(lastBuild))
        {
            if (!EditorUtility.DisplayDialog(
            T("app.name"),
            T("dialog.no_sdk_build_continue"),
            T("btn.continue_anyway"),
            T("btn.cancel")))
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
        T("dialog.pick_hotswap_file"),
        Application.dataPath,
        new[] { T("filter.world_files"), "vrcw", T("filter.all_files"), "*" });

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
        T("app.name"),
        T("progress.preparing_world"),
        AbroProgressRecovered,
        readyPath =>
        {
            if (ConsumeCancelIfRequested(T("dialog.hotswap_cancelled")))
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
        EditorUtility.DisplayDialog(T("app.name"), T("dialog.sdk_missing"), T("btn.ok"));
        return;
#else
        if (operationBusy)
        {
            EditorUtility.DisplayDialog(
            T("app.name"),
            T("dialog.busy"),
            T("btn.ok"));
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

            EditorUtility.DisplayDialog(T("app.name"),
            T("dialog.nothing_ready_upload"),
            T("btn.ok"));
            return;
        }
        UploadLastBuildAsync();
#endif
    }

    [MenuItem("VRCW Hotswap/About VRCW Hotswap", false, 50)]
    public static void OpenAbout()
    {
        VRCWorldHotswapLoc.PromptFirstRunLanguageIfNeeded();
        VRCWorldHotswapAboutWindow.ShowWindow();
    }

#if VRC_SDK_VRCSDK3
    private static async void UploadLastBuildAsync()
    {

        if (!TryBeginOperation(T("action.upload")))
        return;

        IVRCSdkWorldBuilderApi builder = null;
        EventHandler<(string status, float percentage)> progressHandler = null;
        EventHandler uploadStartHandler = null;

        try
        {
            string lastBuild = SessionState.GetString(SessionLastBuildPathKey, null);
            if (string.IsNullOrEmpty(lastBuild) || !File.Exists(lastBuild))
            {
                EditorUtility.DisplayDialog(T("app.name"),
                T("dialog.nothing_ready_upload_retry"),
                T("btn.ok"));
                return;
            }

            if (!VRCSdkControlPanel.TryGetBuilder(out builder))
            {
                EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                EditorUtility.DisplayDialog(T("app.name"),
                T("dialog.open_sdk_control_panel"),
                T("btn.ok"));
                return;
            }

            if (builder.UploadState == SdkUploadState.Uploading)
            {
                EditorUtility.DisplayDialog(
                T("app.name"),
                T("dialog.vrchat_uploading"),
                T("btn.ok"));
                return;
            }

            var pipeline = FindScenePipelineManager();
            if (pipeline == null || string.IsNullOrWhiteSpace(pipeline.blueprintId))
            {
                EditorUtility.DisplayDialog(T("app.name"),
                T("dialog.scene_needs_world_id"),
                T("btn.ok"));
                return;
            }

            if (!TryGetBuilderWorldData(builder, out VRCWorld world, out string thumbnailPath, out string reflectFail))
            {
                ShowSdkApiMovedDialog(reflectFail);
                return;
            }

            if (string.IsNullOrWhiteSpace(world.Name))
            {
                EditorUtility.DisplayDialog(T("app.name"),
                T("dialog.world_name_required"),
                T("btn.ok"));
                return;
            }

            bool creatingNew = string.IsNullOrWhiteSpace(world.ID);
            if (creatingNew && (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)))
            {
                EditorUtility.DisplayDialog(T("app.name"),
                T("dialog.thumbnail_required"),
                T("btn.ok"));
                return;
            }

            if (!TryValidateHotswapIntegrity(lastBuild, clearSessionOnMismatch: true))
            return;

            long fileBytes = new FileInfo(lastBuild).Length;
            string sizeLabel = FormatByteSize(fileBytes);
            if (fileBytes > WorldUploadMaxBytes)
            {
                string tryLabel = fileBytes > WorldUploadHopelessBytes
                ? T("btn.i_understand_likely_fail")
                : T("btn.try_anyway");
                if (!EditorUtility.DisplayDialog(
                T("app.name"),
                BuildOversizeUploadMessage(fileBytes, sizeLabel),
                tryLabel,
                T("btn.ok")))
                {
                    return;
                }
            }

            if (IsAndroidBuildTarget)
            {
                if (!EditorUtility.DisplayDialog(
                T("app.name.android"),
                TF("dialog.howto_continue", AndroidUploadDisclaimer),
                T("btn.continue"),
                T("btn.cancel")))
                {
                    return;
                }
            }

            if (!EditorUtility.DisplayDialog(
            T("app.name"),
            TF(
                "dialog.upload_confirm",
                UploadPlatformLabel,
                sizeLabel,
                world.Name,
                pipeline.blueprintId,
                creatingNew ? T("dialog.upload_confirm_creates") : T("dialog.upload_confirm_updates")),
            T("btn.upload"),
            T("btn.cancel")))
            {
                return;
            }

            uploadProgressStatus = T("progress.starting_upload");
            uploadProgress01 = 0f;
            cancelRequested = false;
            activeUploadBuilder = builder;
            uploadCts = new CancellationTokenSource();

            uploadStartHandler = (sender, args) =>
            {
                uploadProgressStatus = T("progress.uploading");
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
            T("app.name"),
            $"{uploadProgressStatus} ({sizeLabel})",
            uploadProgress01);

            Debug.Log($"<color=cyan>VRCW Hotswap:</color> uploading ({sizeLabel})...\n");
            await builder.UploadLastBuild(world, thumbnailPath, uploadCts.Token);

            EditorApplication.Beep();
            if (cancelRequested || uploadCts.IsCancellationRequested)
            {
                if (!ConsumeSuppressUploadResultDialog())
                EditorUtility.DisplayDialog(T("app.name"), T("dialog.upload_cancelled"), T("btn.ok"));
            }
            else if (!ConsumeSuppressUploadResultDialog())
            {
                EditorUtility.DisplayDialog(
                T("app.name"),
                T("dialog.upload_finished"),
                T("btn.ok"));
            }
        }
        catch (OperationCanceledException)
        {
            EditorApplication.Beep();
            if (!ConsumeSuppressUploadResultDialog())
            EditorUtility.DisplayDialog(T("app.name"), T("dialog.upload_cancelled"), T("btn.ok"));
            Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> upload cancelled.\n");
        }
        catch (Exception e)
        {
            Debug.LogError("Upload failed:\n" + e + "\n");
            EditorApplication.Beep();
            if (cancelRequested || (uploadCts != null && uploadCts.IsCancellationRequested))
            {
                if (!ConsumeSuppressUploadResultDialog())
                EditorUtility.DisplayDialog(T("app.name"), T("dialog.upload_cancelled"), T("btn.ok"));
                return;
            }
            if (ConsumeSuppressUploadResultDialog())
            return;
            if (LooksLikeSdkApiBreak(e))
            {
                ShowSdkApiMovedDialog(e.GetType().Name + ": " + e.Message);
                return;
            }
            EditorUtility.DisplayDialog(T("app.name"), TF("dialog.upload_failed", e.Message), T("btn.ok"));
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
        string info = string.IsNullOrEmpty(uploadProgressStatus) ? T("progress.uploading") : uploadProgressStatus;
        info += $" ({Mathf.RoundToInt(uploadProgress01 * 100f)}%)";

        if (UpdateCancelableProgress(T("app.name"), info, uploadProgress01))
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
        string body = TF(
        "dialog.sdk_problem.body",
        TestedWorldsSdkVersion2019,
        TestedUnityVersion2019,
        TestedWorldsSdkVersion2,
        TestedUnityVersion2,
        TestedWorldsSdkVersion,
        TestedUnityVersion,
        Application.unityVersion,
        sdkHint,
        string.IsNullOrEmpty(detail) ? T("value.none") : detail);

        Debug.LogError("<color=cyan>VRCW Hotswap:</color> SDK mismatch.\n" + detail + "\n");
        EditorUtility.DisplayDialog(T("app.name.sdk_problem"), body, T("btn.ok"));
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

        return T("sdk.version_unknown");
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
        if (!TryBeginOperation(T("action.inspect")))
        return;

        string vrcwPath = EditorUtility.OpenFilePanelWithFilters(
        T("dialog.select_vrcw_to_inspect"),
        Application.dataPath,
        new[] { T("filter.world_files"), "vrcw", T("filter.all_files"), "*" });
        if (string.IsNullOrEmpty(vrcwPath))
        {
            EndOperation();
            return;
        }

        string tmp = ProjTempPath + "/inspect_world.vrcw";
        EnsureTempDirectory();
        try { File.Delete(tmp); } catch { }
        PrepareUncompressedVrcw(
        vrcwPath,
        tmp,
        T("inspect.title"),
        T("progress.reading"),
        AbroProgressInspect,
        readyPath =>
        {
            try
            {
                if (cancelRequested)
                {
                    if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(tmp); } catch { }
                    ConsumeCancelIfRequested(T("dialog.inspect_cancelled"), mentionUpload: false);
                    return;
                }

                var scan = ScanDecompressedVrcw(readyPath);
                if (scan == null)
                {
                    if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(tmp); } catch { }
                    if (ConsumeCancelIfRequested(T("dialog.inspect_cancelled"), mentionUpload: false))
                    return;
                    FailOperation(
                    T("dialog.inspect_failed_read"),
                    "Inspect failed: scan returned null.\n");
                    return;
                }

                var guess = GuessBundlePlatform(vrcwPath);
                string genVer = null;
                int fsFormat = -1;
                string genDetail = null;
                bool genOk = TryReadUnityFsGeneratorVersion(vrcwPath, out genVer, out fsFormat, out genDetail);
                var sb = new StringBuilder();
                sb.AppendLine(TF("inspect.file", vrcwPath));
                sb.AppendLine(TF("inspect.size", FormatByteSize(new FileInfo(vrcwPath).Length)));
                if (genOk)
                {
                    sb.AppendLine(TF("inspect.built_with_unity", genVer));
                    sb.AppendLine(TF("inspect.bundle_format", fsFormat));
                    if (IsDwrGeneratorVersion(genVer))
                        sb.AppendLine(T("inspect.dwr_yes"));
                    sb.AppendLine(
                        IsSupportedHotswapUnityVersion(genVer)
                            ? TF("inspect.version_check_ok", DescribeAcceptedUnityVersion(genVer))
                            :                             IsDwrGeneratorVersion(genVer)
                                ? TF("inspect.version_check_dwr", PreferredHotswapUnityVersion, Application.unityVersion)
                                : TF("inspect.version_check_wrong", PreferredHotswapUnityVersion, Application.unityVersion));
                }
                else
                sb.AppendLine(TF("inspect.built_with_unity_unreadable", genDetail ?? T("value.na")));
                if (lastCompressionProbeResult == true)
                sb.AppendLine(TF("inspect.compression_uncompressed", lastCompressionProbeDetail));
                else if (lastCompressionProbeResult == false)
                sb.AppendLine(TF("inspect.compression_value", DescribeDetectedCompression(detectedSourceCompression), lastCompressionProbeDetail));
                else
                sb.AppendLine(TF("inspect.compression_unknown", lastCompressionProbeDetail ?? T("value.na")));
                sb.AppendLine(TF("inspect.platform_guess", DescribeBundlePlatformGuess(guess)));
                sb.AppendLine();
                if (!string.IsNullOrEmpty(scan.PipelineBlueprintId))
                sb.AppendLine(TF("inspect.main_world_id", scan.PipelineBlueprintId));
                else
                sb.AppendLine(T("inspect.main_world_id_missing"));
                sb.AppendLine();
                sb.AppendLine(TF("inspect.world_ids_found", scan.WorldIds.Count));
                foreach (var id in scan.WorldIds)
                sb.AppendLine($" {id} (x{scan.WorldIdCounts[id]})");
                sb.AppendLine();
                sb.AppendLine(TF("inspect.scene_names", scan.BuildPlayerNames.Count));
                foreach (var n in scan.BuildPlayerNames)
                sb.AppendLine($" {n}");
                sb.AppendLine();
                sb.AppendLine(TF("inspect.other_unity_versions", string.Join(", ", scan.UnityVersions)));
                sb.AppendLine();
                sb.AppendLine(T("inspect.extra_world_ids_hint"));

                Debug.Log($"<color=cyan>World file inspect</color>\n{sb}");
                EditorUtility.DisplayDialog(T("inspect.title"), TruncateForDialog(sb.ToString()), T("btn.ok"));
                if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(tmp); } catch { }
                EndOperation();
            }
            catch (Exception e)
            {
                if (!string.Equals(readyPath, vrcwPath, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(tmp); } catch { }
                FailOperation(TF("dialog.inspect_failed", e.Message), "Inspect failed:\n" + e + "\n");
            }
        },
        onFailed: EndOperation);
    }

    private static string TruncateForDialog(string text, int max = 1500)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + T("inspect.truncated_suffix");
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
        VRCWorldHotswapLoc.PromptFirstRunLanguageIfNeeded();
#if !VRC_SDK_VRCSDK3
        EditorUtility.DisplayDialog(T("app.name"), T("dialog.sdk_missing"), T("btn.ok"));
        return;
#else
        var existing = FindFirstSceneObject<VRCSceneDescriptor>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("<color=cyan>This scene already has a VRCWorld setup.</color>\n");
            EditorUtility.DisplayDialog(T("app.name"),
            T("dialog.scene_has_world_setup"),
            T("btn.ok"));
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
#if UNITY_2022_2_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }

    private static T[] FindSceneObjects<T>() where T : UnityEngine.Object
    {
#if UNITY_2022_2_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType<T>();
#endif
    }

    private static void AnalyzeAndRewrite()
    {
        string sourcePath = string.IsNullOrEmpty(activeUncompressedRecoveredPath)
        ? DecompRecoveredPath
        : activeUncompressedRecoveredPath;

        var recovered = ScanDecompressedVrcw(sourcePath);
        if (recovered == null)
        {
            ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_scanning"));
            return;
        }

        if (recovered.WorldIds.Count == 0)
        {
            FailOperation(
            T("dialog.no_world_ids"),
            "No world IDs found in that .vrcw.\n");
            return;
        }

        string oldWorldId = ChooseOldBlueprintId(recovered);
        if (string.IsNullOrEmpty(oldWorldId))
        {
            Debug.LogWarning("VRCW Hotswap cancelled (no world ID selected).\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog(T("app.name"), T("dialog.cancelled_no_world_id"), T("btn.ok"));
            EndOperation();
            return;
        }

        if (oldWorldId == pendingNewWorldId)
        {
            EditorUtility.DisplayDialog(T("app.name"),
            T("dialog.same_world_id"),
            T("btn.ok"));
        }

        if (oldWorldId.Length != pendingNewWorldId.Length)
        {
            FailOperation(
            TF("dialog.world_id_length_mismatch", oldWorldId.Length, pendingNewWorldId.Length),
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
                ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_updating_ids"));
                return;
            }
        }
        catch (Exception e)
        {
            FailOperation(
            TF("dialog.update_ids_failed", e.Message),
            "Failed while updating IDs:\n" + e.Message + "\n");
            return;
        }

        if (cancelRequested)
        {
            ConsumeCancelIfRequested(T("dialog.hotswap_cancelled"));
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
        T("idpicker.title"),
        TF("dialog.multiple_world_ids", scan.WorldIds.Count),
        T("btn.pick_id"),
        T("btn.cancel"),
        T("btn.use_first"));

        if (pick == 1) return null;
        if (pick == 2) return scan.WorldIds[0];

        return VRCWorldHotswapIdPicker.ShowModal(scan.WorldIds, scan.WorldIdCounts);
    }

    public enum DetectedBundleCompression
    {
        Unknown,
        Uncompressed,
        Lz4,
        Lzma,
        Mixed
    }

    private static void CompressAndFinalize()
    {
        EnsureTempDirectory();

        string compressorPath = GetCompressorExePath();
        bool hasCompressor = !string.IsNullOrEmpty(compressorPath) && File.Exists(compressorPath);

        if (!hasCompressor)
        {
            Debug.LogWarning(
            "<color=cyan>VRCW Hotswap:</color> compressor missing; packing with Unity LZ4Runtime only.\n" +
            "Expected: Assets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe\n");
            if (!EditorUtility.DisplayDialog(
            T("app.name.packing"),
            T("dialog.compressor_missing"),
            T("btn.lz4runtime"),
            T("btn.cancel")))
            {
                CleanupTempFiles();
                EditorApplication.Beep();
                EditorUtility.DisplayDialog(T("app.name"), T("dialog.cancelled_nothing_uploaded"), T("btn.ok"));
                EndOperation();
                return;
            }

            try { File.Delete(TmpOutPath); } catch { }
            int fallbackOp = BeginAsyncOp();
            StartUnityLz4RuntimePack(fallbackOp);
            return;
        }

        var packCtx = BuildPackPickerContext(hasCompressor);

        Debug.Log(
        "<color=cyan>VRCW Hotswap:</color> pack advice: " + packCtx.RecommendReason +
        " (est LZ4 " + FormatByteSize(packCtx.EstLz4Bytes) +
        ", est LZMA " + FormatByteSize(packCtx.EstLzmaBytes) +
        ", unc " + FormatByteSize(packCtx.UncompressedBytes) + ")\n");

        var choice = VRCWorldHotswapPackPicker.ShowModal(packCtx);
        if (choice == VRCWorldHotswapPackPicker.Choice.Cancel)
        {
            CleanupTempFiles();
            EditorApplication.Beep();
            EditorUtility.DisplayDialog(T("app.name"), T("dialog.cancelled_nothing_uploaded"), T("btn.ok"));
            EndOperation();
            return;
        }

        if (choice == VRCWorldHotswapPackPicker.Choice.Lzma)
        {
            if (!EditorUtility.DisplayDialog(
            T("app.name.lzma"),
            T("dialog.lzma_disclaimer"),
            T("btn.lzma"),
            T("btn.cancel")))
            {
                CleanupTempFiles();
                EditorApplication.Beep();
                EditorUtility.DisplayDialog(T("app.name"), T("dialog.cancelled_nothing_uploaded"), T("btn.ok"));
                EndOperation();
                return;
            }
        }

        try { File.Delete(TmpOutPath); } catch { }
        int op = BeginAsyncOp();
        packAsyncOp = op;

        if (choice == VRCWorldHotswapPackPicker.Choice.Lz4Runtime)
        {
            StartUnityLz4RuntimePack(op);
            return;
        }

        if (choice == VRCWorldHotswapPackPicker.Choice.Uncompressed)
        {
            StartUnityUncompressedPack(op);
            return;
        }

        string packMode = choice == VRCWorldHotswapPackPicker.Choice.Lzma ? "lzma" : "lz4";
        packCompressionLabel = choice == VRCWorldHotswapPackPicker.Choice.Lzma ? "LZMA" : "LZ4";
        packProgress01 = 0f;
        packProcessExited = false;
        packProcessExitCode = -1;
        EditorUtility.DisplayCancelableProgressBar(T("app.name"), TF("progress.packing", packCompressionLabel), 0f);

        if (TryStartExternalPack(compressorPath, packMode))
        return;

        Debug.LogWarning(
        "<color=cyan>VRCW Hotswap:</color> AssetsTools pack failed to start; falling back to Unity LZ4Runtime.\n");
        StartUnityLz4RuntimePack(op);
    }

    public struct PackPickerContext
    {
        public bool HasCompressor;
        public bool AdvancedMode;
        public DetectedBundleCompression Detected;
        public long SourceBytes;
        public long UncompressedBytes;
        public long EstLz4Bytes;
        public long EstLzmaBytes;
        public long MaxBytes;
        public long UnlikelyBytes;
        public long HopelessBytes;
        public bool IsAndroid;
        public string PlatformLabel;
        public VRCWorldHotswapPackPicker.Choice Recommended;
        public string RecommendReason;
        public VRCWorldHotswapPackPicker.Choice MatchSource;
    }

    public static bool GetPackAdvancedMode()
    {
        if (EditorPrefs.HasKey(PrefsPackAdvancedModeKey))
        return EditorPrefs.GetBool(PrefsPackAdvancedModeKey, false);

        bool legacyFast = EditorPrefs.GetBool(PrefsPackFastModeKeyLegacy, false);
        if (legacyFast)
        EditorPrefs.SetBool(PrefsPackAdvancedModeKey, true);
        if (EditorPrefs.HasKey(PrefsPackFastModeKeyLegacy))
        EditorPrefs.DeleteKey(PrefsPackFastModeKeyLegacy);
        return legacyFast;
    }

    public static void SetPackAdvancedMode(bool enabled) =>
    EditorPrefs.SetBool(PrefsPackAdvancedModeKey, enabled);

    public static PackPickerContext BuildPackPickerContext(bool hasCompressor)
    {
        long uncBytes = 0;
        try
        {
            if (File.Exists(DecompModPath))
                uncBytes = new FileInfo(DecompModPath).Length;
            else if (File.Exists(DecompRecoveredPath))
                uncBytes = new FileInfo(DecompRecoveredPath).Length;
        }
        catch { }

        long srcBytes = sourceFileBytes;
        if (srcBytes <= 0 && !string.IsNullOrEmpty(pendingRecoveredPath))
        {
            try
            {
                if (File.Exists(pendingRecoveredPath))
                    srcBytes = new FileInfo(pendingRecoveredPath).Length;
            }
            catch { }
        }

        long estLz4 = EstimateLz4PackedBytes(detectedSourceCompression, srcBytes, uncBytes);
        long estLzma = EstimateLzmaPackedBytes(detectedSourceCompression, srcBytes, uncBytes);
        bool advancedMode = GetPackAdvancedMode();
        var matchSource = MatchSourcePackChoice(detectedSourceCompression);

        ComputePackRecommendation(
        hasCompressor,
        estLz4,
        estLzma,
        out var recommended,
        out string reason);

        return new PackPickerContext
        {
            HasCompressor = hasCompressor,
            AdvancedMode = advancedMode,
            Detected = detectedSourceCompression,
            SourceBytes = srcBytes,
            UncompressedBytes = uncBytes,
            EstLz4Bytes = estLz4,
            EstLzmaBytes = estLzma,
            MaxBytes = WorldUploadMaxBytes,
            UnlikelyBytes = WorldUploadUnlikelyBytes,
            HopelessBytes = WorldUploadHopelessBytes,
            IsAndroid = IsAndroidBuildTarget,
            PlatformLabel = UploadPlatformLabel,
            Recommended = recommended,
            RecommendReason = reason,
            MatchSource = matchSource
        };
    }

    public static VRCWorldHotswapPackPicker.Choice MatchSourcePackChoice(DetectedBundleCompression detected)
    {
        switch (detected)
        {
            case DetectedBundleCompression.Uncompressed:
                return VRCWorldHotswapPackPicker.Choice.Uncompressed;
            case DetectedBundleCompression.Lz4:
                return VRCWorldHotswapPackPicker.Choice.Lz4;
            case DetectedBundleCompression.Lzma:
                return VRCWorldHotswapPackPicker.Choice.Lzma;
            default:
                return VRCWorldHotswapPackPicker.Choice.Cancel;
        }
    }

    public static long EstimateLz4PackedBytes(
    DetectedBundleCompression detected,
    long sourceBytes,
    long uncompressedBytes)
    {
        if (detected == DetectedBundleCompression.Lz4 && sourceBytes > 64)
        return sourceBytes;

        if (detected == DetectedBundleCompression.Lzma && sourceBytes > 64)
        {
            long fromUnc = uncompressedBytes > 64 ? (long)(uncompressedBytes * 0.72) : 0;
            long fromSrc = (long)(sourceBytes * 1.35);
            return Math.Max(sourceBytes, Math.Max(fromUnc, fromSrc));
        }

        if (uncompressedBytes > 64)
        return Math.Max(64, (long)(uncompressedBytes * 0.70));

        if (sourceBytes > 64)
        return sourceBytes;

        return 0;
    }

    public static long EstimateLzmaPackedBytes(
    DetectedBundleCompression detected,
    long sourceBytes,
    long uncompressedBytes)
    {
        if (detected == DetectedBundleCompression.Lzma && sourceBytes > 64)
        return sourceBytes;

        if (detected == DetectedBundleCompression.Lz4 && sourceBytes > 64)
        return Math.Max(64, (long)(sourceBytes * 0.62));

        if (uncompressedBytes > 64)
        return Math.Max(64, (long)(uncompressedBytes * 0.55));

        if (sourceBytes > 64)
        return Math.Max(64, (long)(sourceBytes * 0.62));

        return 0;
    }

    public static void ComputePackRecommendation(
    bool hasCompressor,
    long estLz4,
    long estLzma,
    out VRCWorldHotswapPackPicker.Choice recommended,
    out string reason)
    {
        if (!hasCompressor)
        {
            recommended = VRCWorldHotswapPackPicker.Choice.Lz4Runtime;
            reason = T("pack.reason.compressor_missing");
            return;
        }

        long maxBytes = WorldUploadMaxBytes;
        long unlikelyBytes = WorldUploadUnlikelyBytes;
        long hopelessBytes = WorldUploadHopelessBytes;

        if (estLz4 <= 0 && estLzma <= 0)
        {
            recommended = VRCWorldHotswapPackPicker.Choice.Lz4;
            reason = T("pack.reason.size_unknown");
            return;
        }

        if (estLz4 > 0 && estLz4 <= maxBytes)
        {
            recommended = VRCWorldHotswapPackPicker.Choice.Lz4;
            reason = TF("pack.reason.lz4_under_limit", UploadPlatformLabel, FormatByteSize(maxBytes));
            return;
        }

        if (IsAndroidBuildTarget && estLz4 > maxBytes)
        {
            recommended = VRCWorldHotswapPackPicker.Choice.Lzma;
            reason = estLzma > 0 && estLzma <= maxBytes
            ? T("pack.reason.android_lzma_fit")
            : T("pack.reason.android_lzma_maybe");
            return;
        }

        if (estLz4 > 0 && estLz4 <= unlikelyBytes)
        {
            recommended = VRCWorldHotswapPackPicker.Choice.Lz4;
            reason = TF("pack.reason.lz4_soft_zone", FormatByteSize(maxBytes), FormatByteSize(unlikelyBytes));
            return;
        }

        recommended = VRCWorldHotswapPackPicker.Choice.Lzma;
        if (estLzma > hopelessBytes)
        {
            reason = T("pack.reason.lzma_hopeless");
        }
        else if (estLzma > unlikelyBytes)
        {
            reason = T("pack.reason.lzma_soft");
        }
        else
        {
            reason = T("pack.reason.lzma_under_limit");
        }
    }

    private static string GetCompressorExePath()
    {
        return Path.GetFullPath(Path.Combine(
        Application.dataPath,
        "VRCW Hotswap",
        "Editor",
        "Compressor",
        "VRCWHotswapCompressor.exe"));
    }

    private static bool TryStartExternalPack(string compressorPath, string packMode)
    {
        try
        {
            TryKillPackProcess();
            packProgress01 = 0f;
            packProcessExited = false;
            packProcessExitCode = -1;

            var startInfo = new ProcessStartInfo
            {
                FileName = compressorPath,
                Arguments = "c \"" + DecompModPath + "\" \"" + TmpOutPath + "\" " + packMode,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                if (float.TryParse(e.Data, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                {
                    float p = pct / 100f;
                    if (p < 0f) p = 0f;
                    if (p > 1f) p = 1f;
                    packProgress01 = p;
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.LogWarning("<color=cyan>VRCW Hotswap compressor:</color> " + e.Data + "\n");
            };
            process.Exited += (_, __) =>
            {
                try { packProcessExitCode = process.ExitCode; } catch { packProcessExitCode = -1; }
                packProcessExited = true;
            };

            if (!process.Start())
            {
                Debug.LogWarning("<color=cyan>VRCW Hotswap:</color> failed to start compressor; falling back to Unity LZ4Runtime.\n");
                return false;
            }

            packProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            EditorApplication.update += PackProcessProgress;
            Debug.Log("<color=cyan>VRCW Hotswap:</color> packing with AssetsTools " + packCompressionLabel + ".\n");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
            "<color=cyan>VRCW Hotswap:</color> compressor failed to start (" + e.Message + "); falling back to Unity LZ4Runtime.\n");
            TryKillPackProcess();
            return false;
        }
    }

    private static void PackProcessProgress()
    {
        if (!IsCurrentAsyncOp(packAsyncOp))
        {
            EditorApplication.update -= PackProcessProgress;
            TryKillPackProcess();
            return;
        }

        if (UpdateCancelableProgress(T("app.name"), TF("progress.packing", packCompressionLabel), packProgress01))
        {
            EditorApplication.update -= PackProcessProgress;
            TryKillPackProcess();
            ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_packing"));
            return;
        }

        if (!packProcessExited)
        return;

        EditorApplication.update -= PackProcessProgress;
        TryKillPackProcess();
        EditorUtility.ClearProgressBar();

        if (!IsCurrentAsyncOp(packAsyncOp))
        {
            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);
            File.Delete(TmpOutPath);
            return;
        }

        if (ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_packing")))
        return;

        bool ok = packProcessExitCode == 0 && File.Exists(TmpOutPath) && new FileInfo(TmpOutPath).Length > 64;
        if (ok)
        {
            AfterCompress();
            return;
        }

        Debug.LogWarning(
        "<color=cyan>VRCW Hotswap:</color> AssetsTools " + packCompressionLabel +
        " packing failed (exit " + packProcessExitCode + "); falling back to Unity LZ4Runtime.\n");
        try { File.Delete(TmpOutPath); } catch { }
        StartUnityLz4RuntimePack(packAsyncOp);
    }

    private static void StartUnityLz4RuntimePack(int op)
    {
        packAsyncOp = op;
        packCompressionLabel = "Unity LZ4Runtime";
        EditorUtility.DisplayCancelableProgressBar(T("app.name"), T("progress.packing_unity_lz4runtime"), 0f);
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

            if (ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_packing")))
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
            T("app.name"),
            TF("dialog.packing_failed", unityResult),
            T("btn.ok"));
            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);
            File.Delete(TmpOutPath);
            EndOperation();
        };
    }

    private static void StartUnityUncompressedPack(int op)
    {
        packAsyncOp = op;
        packCompressionLabel = "Uncompressed";
        EditorUtility.DisplayCancelableProgressBar(T("app.name"), T("progress.packing_uncompressed"), 0f);
        abro = AssetBundle.RecompressAssetBundleAsync(DecompModPath, TmpOutPath, BuildCompression.UncompressedRuntime);
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

            if (ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_packing")))
            return;

            if (unityOk)
            {
                AfterCompress();
                return;
            }

            Debug.LogError(
            $"VRCW Hotswap failed: Unity uncompressed packing failed ({unityResult}).\n" +
            "The modified file could not be written. Try again, or use a different .vrcw.\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog(
            T("app.name"),
            TF("dialog.packing_failed", unityResult),
            T("btn.ok"));
            File.Delete(DecompRecoveredPath);
            File.Delete(DecompModPath);
            File.Delete(TmpOutPath);
            EndOperation();
        };
    }

    private static void TryKillPackProcess()
    {
        try
        {
            if (packProcess != null && !packProcess.HasExited)
                packProcess.Kill();
        }
        catch { }
        try { packProcess?.Dispose(); } catch { }
        packProcess = null;
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
            T("dialog.invalid_packed_file"),
            $"VRCW Hotswap failed: recompression produced empty/invalid output ({badBytes} bytes).\n");
            return;
        }

        int op = BeginAsyncOp();
        abcr = AssetBundle.LoadFromFileAsync(TmpOutPath);
        EditorUtility.DisplayCancelableProgressBar(T("app.name"), T("progress.checking_result"), 0f);
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

            if (ConsumeCancelIfRequested(T("dialog.hotswap_cancelled_checking")))
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
                T("dialog.packed_file_open_failed"),
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
                T("dialog.packed_file_no_assets"),
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
            dest = EditorUtility.SaveFilePanel(T("dialog.save_hotswapped_world"), Path.GetDirectoryName(dest), Path.GetFileName(dest), "vrcw");
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
            TF("dialog.write_failed", e.Message),
            "Failed to write hotswapped VRCW:\n" + e.Message + "\n");
            return;
        }
        finally
        {
            File.Delete(TmpOutPath);
        }

        EditorApplication.Beep();
        long outBytes = File.Exists(dest) ? new FileInfo(dest).Length : 0;
        string sizeNote = outBytes > 0 ? TF("inspect.size", FormatByteSize(outBytes)) + "\n\n" : "";
        string sizeWarning = outBytes > WorldUploadMaxBytes
        ? BuildOversizeHint(outBytes) + "\n\n"
        : "";
        string androidPackNote = BuildAndroidPackedSizeNote(outBytes);
        string androidNote = IsAndroidBuildTarget
        ? AndroidUploadDisclaimer + "\n\n"
        : "";
        string lzmaNote = string.Equals(packCompressionLabel, "LZMA", StringComparison.Ordinal)
        ? T("dialog.lzma_join_note")
        : "";
        EditorUtility.DisplayDialog(
        T("app.name"),
        TF("dialog.world_loaded", sizeNote, androidPackNote, sizeWarning, androidNote, lzmaNote, pendingNewWorldId),
        T("btn.ok"));
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
        return TF("android.packed_ok", packedLabel, limitLabel);

        return TF("android.packed_over", packedLabel, limitLabel);
    }

    private static string BuildOversizeUploadMessage(long fileBytes, string sizeLabel)
    {
        return TF("oversize.upload_message", UploadPlatformLabel, sizeLabel, FormatByteSize(WorldUploadMaxBytes), BuildOversizeHint(fileBytes));
    }

    private static string BuildOversizeHint(long fileBytes)
    {
        if (IsAndroidBuildTarget)
        {
            if (fileBytes > AndroidUploadHopelessBytes)
            return T("oversize.android_over");

            return T("oversize.android_packed");
        }

        if (fileBytes > WorldUploadHopelessBytes)
        return T("oversize.pc_hopeless");

        if (fileBytes > WorldUploadUnlikelyBytes)
        return T("oversize.pc_unlikely");

        return T("oversize.pc_maybe");
    }

    public static string FormatByteSize(long bytes)
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
            T("app.name"),
            T("dialog.nothing_to_reset"),
            T("btn.ok"));
            return;
        }

        string confirm =
        uploadInFlight
        ? T("dialog.reset_confirm_upload_running")
        : operationBusy
        ? T("dialog.reset_confirm_busy")
        : T("dialog.reset_confirm_idle");

        if (!EditorUtility.DisplayDialog(
        T("app.name"),
        confirm,
        uploadInFlight || operationBusy ? T("btn.cancel_and_reset") : T("btn.reset"),
        T("btn.keep_going")))
        {
            return;
        }

        try
        {
            AbortAllWorkForReset();

            EditorApplication.Beep();
            EditorUtility.DisplayDialog(
            T("app.name"),
            uploadInFlight
            ? T("dialog.reset_done_upload")
            : T("dialog.reset_done"),
            T("btn.ok"));
            Debug.Log("<color=cyan>VRCW Hotswap reset.</color>\n");
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to reset hotswap:\n" + e.Message + "\n");
            EditorApplication.Beep();
            EditorUtility.DisplayDialog(T("app.name"), TF("dialog.reset_failed", e.Message), T("btn.ok"));
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
        TryKillPackProcess();

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
        TryKillPackProcess();
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
            T("app.name"),
            T("dialog.hotswapped_missing"),
            T("btn.ok"));
            return false;
        }

        long size = new FileInfo(path).Length;
        if (!long.TryParse(expectedSize, out long wantSize) || size != wantSize)
        {
            if (clearSessionOnMismatch) ClearHotswapSessionFlags();
            EditorUtility.DisplayDialog(
            T("app.name"),
            T("dialog.hotswapped_changed"),
            T("btn.ok"));
            return false;
        }

        string fp = ComputeQuickFileFingerprint(path);
        if (!string.Equals(fp, expectedFp, StringComparison.Ordinal))
        {
            if (clearSessionOnMismatch) ClearHotswapSessionFlags();
            EditorUtility.DisplayDialog(
            T("app.name"),
            T("dialog.hotswapped_changed"),
            T("btn.ok"));
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
        VRCWorldHotswapLoc.PromptFirstRunLanguageIfNeeded();

        if (operationBusy)
        {
            EditorUtility.DisplayDialog(
            T("app.name"),
            TF("dialog.already_busy", label),
            T("btn.ok"));
            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(T("app.name"), T("dialog.exit_play_mode"), T("btn.ok"));
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
        EditorUtility.DisplayDialog(T("app.name"), dialogMessage, T("btn.ok"));
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
        string body = mentionUpload ? message + T("cancel.nothing_uploaded_suffix") : message;
        EditorUtility.DisplayDialog(T("app.name"), body, T("btn.ok"));
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

        if (string.Equals(generatorVersion, PreferredHotswapUnityVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(generatorVersion, Application.unityVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeAcceptedUnityVersion(string generatorVersion)
    {
        if (string.Equals(generatorVersion, PreferredHotswapUnityVersion, StringComparison.OrdinalIgnoreCase))
            return TF("unity.supported.preferred", PreferredHotswapUnityVersion);

        if (string.Equals(generatorVersion, Application.unityVersion, StringComparison.OrdinalIgnoreCase))
            return TF("unity.supported.editor", Application.unityVersion);

        return generatorVersion;
    }

    private static bool ConfirmUnityVersionOrContinue(string vrcwPath)
    {
        if (!TryReadUnityFsGeneratorVersion(vrcwPath, out string generatorVersion, out int _, out string detail))
        {
            return EditorUtility.DisplayDialog(
            T("app.name.unity_version"),
            TF("confirm.unity_unknown", detail ?? T("value.na"), PreferredHotswapUnityVersion),
            T("btn.continue_anyway"),
            T("btn.cancel"));
        }

        if (IsSupportedHotswapUnityVersion(generatorVersion))
        {
            Debug.Log(
                $"<color=cyan>VRCW Hotswap:</color> Unity version OK " +
                $"({DescribeAcceptedUnityVersion(generatorVersion)}).\n");
            return true;
        }

        string editorVer = Application.unityVersion;

        if (IsDwrGeneratorVersion(generatorVersion))
        {
            Debug.LogWarning(
                $"<color=cyan>VRCW Hotswap:</color> DWR bundle detected: file={generatorVersion}, " +
                $"editor={editorVer}.\n");

            return EditorUtility.DisplayDialog(
                T("app.name.dwr_bundle"),
                TF("confirm.dwr_bundle", generatorVersion, editorVer, PreferredHotswapUnityVersion),
                T("btn.continue_anyway"),
                T("btn.cancel"));
        }

        string body = TF("confirm.unity_mismatch", generatorVersion, editorVer, PreferredHotswapUnityVersion);

        Debug.LogWarning(
            $"<color=cyan>VRCW Hotswap:</color> Unity mismatch: file={generatorVersion}, " +
            $"editor={editorVer}, preferred={PreferredHotswapUnityVersion}.\n");

        return EditorUtility.DisplayDialog(
            T("app.name.unity_mismatch"),
            body,
            T("btn.continue_anyway"),
            T("btn.cancel"));
    }

    private static bool IsDwrGeneratorVersion(string generatorVersion)
    {
        return !string.IsNullOrEmpty(generatorVersion) &&
               generatorVersion.IndexOf("DWR", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ConfirmPlatformMatchOrContinue(string vrcwPath)
    {
        var guess = GuessBundlePlatform(vrcwPath);
        bool androidTarget = IsAndroidBuildTarget;
        string targetLabel = androidTarget ? T("platform.android") : T("platform.pc");

        if (guess == BundlePlatformGuess.Unknown || guess == BundlePlatformGuess.Ambiguous)
        {
            string why = guess == BundlePlatformGuess.Ambiguous
            ? TF("confirm.platform_ambiguous", targetLabel)
            : TF("confirm.platform_unknown", targetLabel);

            return EditorUtility.DisplayDialog(
            T("app.name.platform"),
            why,
            T("btn.continue"),
            T("btn.cancel"));
        }

        if (androidTarget && guess == BundlePlatformGuess.Pc)
        {
            return EditorUtility.DisplayDialog(
            T("app.name.wrong_platform"),
            T("confirm.platform_android_target_pc_file"),
            T("btn.continue_anyway"),
            T("btn.cancel"));
        }

        if (!androidTarget && guess == BundlePlatformGuess.Android)
        {
            return EditorUtility.DisplayDialog(
            T("app.name.wrong_platform"),
            T("confirm.platform_pc_target_android_file"),
            T("btn.continue_anyway"),
            T("btn.cancel"));
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
            T("app.name.pc_size"),
            TF("confirm.pc_size_soft", FormatByteSize(bytes)),
            T("btn.continue_anyway"),
            T("btn.cancel"));
        }

        if (bytes <= PcUploadHopelessBytes)
        {
            return EditorUtility.DisplayDialog(
            T("app.name.pc_size"),
            TF("confirm.pc_size_unlikely", FormatByteSize(bytes)),
            T("btn.continue_anyway"),
            T("btn.cancel"));
        }

        return EditorUtility.DisplayDialog(
        T("app.name.pc_size"),
        TF("confirm.pc_size_hopeless", FormatByteSize(bytes)),
        T("btn.i_understand_likely_fail"),
        T("btn.cancel"));
    }

    private static bool ConfirmAndroidSourceSizeOrContinue(string vrcwPath)
    {
        long bytes = new FileInfo(vrcwPath).Length;
        if (bytes <= AndroidPracticalSourceMaxBytes)
        return true;

        if (bytes <= AndroidTrySourceMaxBytes)
        {
            return EditorUtility.DisplayDialog(
            T("app.name.android_size"),
            TF("confirm.android_size_try", FormatByteSize(bytes)),
            T("btn.try_packing"),
            T("btn.cancel"));
        }

        return EditorUtility.DisplayDialog(
        T("app.name.android_size"),
        TF("confirm.android_size_hopeless", FormatByteSize(bytes)),
        T("btn.i_understand_likely_fail"),
        T("btn.cancel"));
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

    private static void EnsureTempDirectory()
    {
        try
        {
            if (!string.IsNullOrEmpty(ProjTempPath))
                Directory.CreateDirectory(ProjTempPath);
        }
        catch { }
    }

    private static void CleanupTempFiles()
    {
        EnsureTempDirectory();
        try { File.Delete(DecompRecoveredPath); } catch { }
        try { File.Delete(DecompModPath); } catch { }
        try { File.Delete(TmpOutPath); } catch { }
    }

    private static void AbroProgressRecovered()
    {
        if (abro != null)
        UpdateCancelableProgress(T("app.name"), T("progress.preparing_world"), abro.progress);
    }

    private static void AbroProgressCompress()
    {
        if (abro != null)
        UpdateCancelableProgress(T("app.name"), T("progress.packing_unity_lz4runtime"), abro.progress);
    }

    private static void AbroProgressInspect()
    {
        if (abro != null)
        UpdateCancelableProgress(T("inspect.title"), T("progress.reading"), abro.progress);
    }

    private static void AbcrProgress()
    {
        if (abcr != null)
        UpdateCancelableProgress(T("app.name"), T("progress.checking_result"), abcr.progress);
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
        detectedSourceCompression = TryDetectBundleCompression(vrcwPath, out lastCompressionProbeDetail);
        try { sourceFileBytes = new FileInfo(vrcwPath).Length; } catch { sourceFileBytes = 0; }
        lastCompressionProbeResult = detectedSourceCompression == DetectedBundleCompression.Uncompressed
        ? true
        : detectedSourceCompression == DetectedBundleCompression.Unknown
        ? (bool?)null
        : false;

        if (detectedSourceCompression == DetectedBundleCompression.Uncompressed)
        {
            Debug.Log(
            $"<color=cyan>VRCW Hotswap:</color> already uncompressed ({lastCompressionProbeDetail}); skipping decompress.\n");
            onReady(vrcwPath);
            return;
        }

        Debug.Log(
        $"<color=cyan>VRCW Hotswap:</color> decompressing with Unity " +
        $"({lastCompressionProbeDetail ?? "compression unknown / not proven uncompressed"})...\n");

        EnsureTempDirectory();
        try { File.Delete(tempUncompressedPath); } catch { }
        int op = BeginAsyncOp();
        abro = AssetBundle.RecompressAssetBundleAsync(vrcwPath, tempUncompressedPath, BuildCompression.UncompressedRuntime);
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
                ConsumeCancelIfRequested(T("dialog.preparing_cancelled"));
                else
                onFailed?.Invoke();
                return;
            }

            if (!ok)
            {
                try { File.Delete(tempUncompressedPath); } catch { }
                FailOperation(
                TF("dialog.open_vrcw_failed", string.IsNullOrEmpty(result) ? T("error.unknown") : result),
                $"Could not open that .vrcw file.\n{result}\n");

                return;
            }

            onReady(tempUncompressedPath);
        };
    }

    public static string DescribeDetectedCompression(DetectedBundleCompression kind)
    {
        switch (kind)
        {
            case DetectedBundleCompression.Uncompressed: return T("compression.uncompressed");
            case DetectedBundleCompression.Lz4: return "LZ4";
            case DetectedBundleCompression.Lzma: return "LZMA";
            case DetectedBundleCompression.Mixed: return T("compression.mixed");
            default: return T("compression.unknown");
        }
    }

    private static string DescribeBundlePlatformGuess(BundlePlatformGuess guess)
    {
        switch (guess)
        {
            case BundlePlatformGuess.Pc: return T("platform.guess.pc");
            case BundlePlatformGuess.Android: return T("platform.guess.android");
            case BundlePlatformGuess.Ambiguous: return T("platform.guess.ambiguous");
            default: return T("platform.guess.unknown");
        }
    }

    private static DetectedBundleCompression CompressionTypeFromFlag(int compType)
    {
        if (compType == 1) return DetectedBundleCompression.Lzma;
        if (compType == 2 || compType == 3) return DetectedBundleCompression.Lz4;
        if (compType == 0) return DetectedBundleCompression.Uncompressed;
        return DetectedBundleCompression.Unknown;
    }

    private static DetectedBundleCompression TryDetectBundleCompression(string path, out string detail)
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
                    return DetectedBundleCompression.Unknown;
                }

                string sig = Encoding.ASCII.GetString(sigBytes).TrimEnd('\0');
                if (sig != "UnityFS")
                {
                    detail = "not UnityFS (" + sig + ")";
                    return DetectedBundleCompression.Unknown;
                }

                uint format = ReadUInt32BE(br);
                if (format < 6 || format > 8)
                {
                    if (format < 3)
                    {
                        detail = "unsupported UnityFS format " + format;
                        return DetectedBundleCompression.Unknown;
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
                    return DetectedBundleCompression.Unknown;
                }

                if (infoCompType != 0)
                {
                    var fromInfo = CompressionTypeFromFlag(infoCompType);
                    detail = "blocks-info compression type=" + infoCompType +
                    " (LZMA=1 LZ4=2 LZ4HC=3)";
                    return fromInfo == DetectedBundleCompression.Uncompressed
                    ? DetectedBundleCompression.Unknown
                    : fromInfo;
                }

                if (compressedInfoSize != uncompressedInfoSize)
                {
                    detail = "blocks-info size mismatch for type=None";
                    return DetectedBundleCompression.Unknown;
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
                        return DetectedBundleCompression.Unknown;
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
                    return DetectedBundleCompression.Unknown;
                }

                int o = 16;
                if (o + 4 > info.Length)
                {
                    detail = "blocks-info too small";
                    return DetectedBundleCompression.Unknown;
                }

                uint blockCount = ReadUInt32BE(info, ref o);
                if (blockCount == 0 || blockCount > 5_000_000u)
                {
                    detail = "suspicious blockCount=" + blockCount;
                    return DetectedBundleCompression.Unknown;
                }

                long expectedMin = 16L + 4L + (long)blockCount * 10L + 4L;
                if (expectedMin > info.Length)
                {
                    detail = "blocks-info truncated for blockCount=" + blockCount;
                    return DetectedBundleCompression.Unknown;
                }

                int noneBlocks = 0;
                int lz4Blocks = 0;
                int lzmaBlocks = 0;
                int otherBlocks = 0;
                for (uint i = 0; i < blockCount; i++)
                {
                    uint uSize = ReadUInt32BE(info, ref o);
                    uint cSize = ReadUInt32BE(info, ref o);
                    ushort blockFlags = ReadUInt16BE(info, ref o);
                    int blockComp = blockFlags & 0x3F;

                    if (blockComp == 0)
                    {
                        noneBlocks++;
                        if (cSize != uSize)
                        {
                            detail = "uncompressed block with mismatched sizes";
                            return DetectedBundleCompression.Unknown;
                        }
                    }
                    else if (blockComp == 1)
                    lzmaBlocks++;
                    else if (blockComp == 2 || blockComp == 3)
                    lz4Blocks++;
                    else
                    otherBlocks++;
                }

                int compressedBlocks = lz4Blocks + lzmaBlocks + otherBlocks;
                if (compressedBlocks == 0)
                {
                    detail = "all " + blockCount + " blocks uncompressed" +
                    (headerFileSize > 0 ? ", headerSize=" + headerFileSize : "");
                    return DetectedBundleCompression.Uncompressed;
                }

                detail = compressedBlocks + " compressed / " + blockCount + " blocks" +
                " (lz4=" + lz4Blocks + ", lzma=" + lzmaBlocks + ", other=" + otherBlocks + ")";

                if (otherBlocks > 0)
                return DetectedBundleCompression.Mixed;
                if (lz4Blocks > 0 && lzmaBlocks > 0)
                return DetectedBundleCompression.Mixed;
                if (lzmaBlocks > 0)
                return DetectedBundleCompression.Lzma;
                if (lz4Blocks > 0)
                return DetectedBundleCompression.Lz4;
                return DetectedBundleCompression.Mixed;
            }
        }
        catch (Exception e)
        {
            detail = e.GetType().Name + ": " + e.Message;
            return DetectedBundleCompression.Unknown;
        }
    }

    private static bool? TryIsFullyUncompressedUnityFs(string path, out string detail)
    {
        var kind = TryDetectBundleCompression(path, out detail);
        if (kind == DetectedBundleCompression.Uncompressed) return true;
        if (kind == DetectedBundleCompression.Unknown) return null;
        return false;
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
                if (UpdateCancelableProgress(T("app.name"), T("progress.scanning_world"),
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
                if (UpdateCancelableProgress(T("app.name"), T("progress.updating_ids"),
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

public class VRCWorldHotswapPackPicker : EditorWindow
{
    public enum Choice
    {
        Cancel,
        Lz4Runtime,
        Lz4,
        Lzma,
        Uncompressed
    }

    private VRCWorldHotswap.PackPickerContext ctx;
    private bool advancedMode;
    private Choice result = Choice.Cancel;
    private Vector2 scroll;

    public static Choice ShowModal(VRCWorldHotswap.PackPickerContext context)
    {
        var window = CreateInstance<VRCWorldHotswapPackPicker>();
        window.titleContent = new GUIContent(VRCWorldHotswapLoc.T("app.name.packing"));
        window.ctx = context;
        window.advancedMode = context.AdvancedMode;
        window.minSize = new Vector2(520, 440);
        window.maxSize = new Vector2(620, 620);
        window.ShowModalUtility();
        return window.result;
    }

    private void PersistMode()
    {
        VRCWorldHotswap.SetPackAdvancedMode(advancedMode);
        ctx.AdvancedMode = advancedMode;
    }

    private static string ChoiceTitle(Choice choice)
    {
        switch (choice)
        {
            case Choice.Uncompressed: return VRCWorldHotswapLoc.T("pack.choice.uncompressed.title");
            case Choice.Lz4Runtime: return VRCWorldHotswapLoc.T("pack.choice.lz4runtime.title");
            case Choice.Lz4: return VRCWorldHotswapLoc.T("pack.choice.lz4.title");
            case Choice.Lzma: return VRCWorldHotswapLoc.T("pack.choice.lzma.title");
            default: return choice.ToString();
        }
    }

    private static string ChoiceSubtitle(Choice choice)
    {
        switch (choice)
        {
            case Choice.Uncompressed:
                return VRCWorldHotswapLoc.T("pack.choice.uncompressed.subtitle");
            case Choice.Lz4Runtime:
                return VRCWorldHotswapLoc.T("pack.choice.lz4runtime.subtitle");
            case Choice.Lz4:
                return VRCWorldHotswapLoc.T("pack.choice.lz4.subtitle");
            case Choice.Lzma:
                return VRCWorldHotswapLoc.T("pack.choice.lzma.subtitle");
            default:
                return "";
        }
    }

    private bool ChoiceEnabled(Choice choice)
    {
        if (choice == Choice.Lz4 || choice == Choice.Lzma)
        return ctx.HasCompressor;
        return true;
    }

    private void DrawPackButton(Choice choice)
    {
        bool recommended = ctx.Recommended == choice;
        bool matchesSource = ctx.MatchSource == choice && ctx.MatchSource != Choice.Cancel;
        bool enabled = ChoiceEnabled(choice);

        Color prev = GUI.backgroundColor;
        if (recommended)
        GUI.backgroundColor = new Color(0.45f, 0.9f, 0.55f);
        else if (matchesSource)
        GUI.backgroundColor = new Color(0.75f, 0.85f, 1f);

        var badges = new List<string>();
        if (recommended) badges.Add(VRCWorldHotswapLoc.T("pack.badge.recommended"));
        if (matchesSource) badges.Add(VRCWorldHotswapLoc.T("pack.badge.matches_source"));
        string label = ChoiceTitle(choice);
        if (badges.Count > 0)
        label += "  [" + string.Join(", ", badges) + "]";

        EditorGUI.BeginDisabledGroup(!enabled);
        if (GUILayout.Button(label, GUILayout.Height(34)))
        {
            result = choice;
            Close();
        }
        EditorGUI.EndDisabledGroup();
        GUI.backgroundColor = prev;
        EditorGUILayout.LabelField(ChoiceSubtitle(choice), EditorStyles.miniLabel);
        GUILayout.Space(4);
    }

    private void OnGUI()
    {
        string detectedLabel = VRCWorldHotswap.DescribeDetectedCompression(ctx.Detected);
        string unlikelyLabel = ctx.IsAndroid ? "" : VRCWorldHotswapLoc.TF("pack.helpbox.unlikely_suffix", VRCWorldHotswap.FormatByteSize(ctx.UnlikelyBytes));
        string sourceSize = ctx.SourceBytes > 0 ? " (" + VRCWorldHotswap.FormatByteSize(ctx.SourceBytes) + ")" : "";
        string uncompressedLabel = ctx.UncompressedBytes > 0 ? VRCWorldHotswap.FormatByteSize(ctx.UncompressedBytes) : VRCWorldHotswapLoc.T("value.na");
        string lz4Label = ctx.EstLz4Bytes > 0 ? VRCWorldHotswap.FormatByteSize(ctx.EstLz4Bytes) : VRCWorldHotswapLoc.T("value.na");
        string lzmaLabel = ctx.EstLzmaBytes > 0 ? VRCWorldHotswap.FormatByteSize(ctx.EstLzmaBytes) : VRCWorldHotswapLoc.T("value.na");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("pack.mode"), GUILayout.Width(40));
        int modeIndex = advancedMode ? 1 : 0;
        int newMode = GUILayout.Toolbar(modeIndex, new[] { VRCWorldHotswapLoc.T("pack.mode.simple"), VRCWorldHotswapLoc.T("pack.mode.advanced") });
        if (newMode != modeIndex)
        {
            advancedMode = newMode == 1;
            PersistMode();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(
        advancedMode
        ? VRCWorldHotswapLoc.T("pack.mode.advanced_desc")
        : VRCWorldHotswapLoc.T("pack.mode.simple_desc"),
        EditorStyles.miniLabel);

        EditorGUILayout.HelpBox(
        VRCWorldHotswapLoc.TF(
            "pack.helpbox",
            ctx.PlatformLabel,
            VRCWorldHotswap.FormatByteSize(ctx.MaxBytes),
            unlikelyLabel,
            detectedLabel,
            sourceSize,
            uncompressedLabel,
            lz4Label,
            lzmaLabel,
            ChoiceTitle(ctx.Recommended),
            ctx.RecommendReason),
        MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("pack.recommended_path"), EditorStyles.boldLabel);
        DrawPackButton(ctx.Recommended);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("pack.other_options"), EditorStyles.boldLabel);
        if (ctx.Recommended != Choice.Lz4)
        DrawPackButton(Choice.Lz4);
        if (ctx.Recommended != Choice.Lzma)
        DrawPackButton(Choice.Lzma);

        if (advancedMode)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("pack.testing_options"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
            VRCWorldHotswapLoc.T("pack.testing_desc"),
            EditorStyles.miniLabel);
            if (ctx.Recommended != Choice.Lz4Runtime)
            DrawPackButton(Choice.Lz4Runtime);
            if (ctx.Recommended != Choice.Uncompressed)
            DrawPackButton(Choice.Uncompressed);
        }

        EditorGUILayout.EndScrollView();

        if (!ctx.HasCompressor)
        {
            EditorGUILayout.HelpBox(
            VRCWorldHotswapLoc.T("pack.compressor_missing_warning"),
            MessageType.Warning);
        }

        if (GUILayout.Button(VRCWorldHotswapLoc.T("btn.cancel"), GUILayout.Height(28)))
        {
            result = Choice.Cancel;
            Close();
        }
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
        window.titleContent = new GUIContent(VRCWorldHotswapLoc.T("idpicker.title"));
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
        VRCWorldHotswapLoc.T("idpicker.help"),
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
        if (GUILayout.Button(VRCWorldHotswapLoc.T("btn.cancel")))
        {
            confirmed = false;
            selected = null;
            Close();
        }
        if (GUILayout.Button(VRCWorldHotswapLoc.T("btn.use_selected")))
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
        var window = GetWindow<VRCWorldHotswapAboutWindow>(true, VRCWorldHotswapLoc.T("about.title"), true);
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
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("app.name"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.TF("about.version", VRCWorldHotswap.Version));
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("about.tested_working"), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            VRCWorldHotswapLoc.TF("about.tested_working_2019", VRCWorldHotswap.TestedWorldsSdkVersion2019, VRCWorldHotswap.TestedUnityVersion2019),
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            VRCWorldHotswapLoc.TF("about.tested_working_2022_6f1", VRCWorldHotswap.TestedWorldsSdkVersion2, VRCWorldHotswap.TestedUnityVersion2),
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            VRCWorldHotswapLoc.TF("about.tested_working_2022_22f1", VRCWorldHotswap.TestedWorldsSdkVersion, VRCWorldHotswap.TestedUnityVersion),
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("about.partially_tested"), EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            VRCWorldHotswapLoc.TF("about.partially_tested_dwr", VRCWorldHotswap.TestedWorldsSdkVersion, VRCWorldHotswap.TestedUnityVersion),
            EditorStyles.miniLabel);
        GUILayout.Space(6);
        EditorGUILayout.HelpBox(
            VRCWorldHotswapLoc.T("about.description"),
        MessageType.Info);

        if (GUILayout.Button(VRCWorldHotswapLoc.T("btn.show_howto_again"), GUILayout.Height(26)))
        {
            VRCWorldHotswap.ResetHowtoPref();
            VRCWorldHotswap.ShowHowtoDialog();
        }

        GUILayout.Space(12);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("about.credits"), EditorStyles.boldLabel);
        DrawLinkedLine(VRCWorldHotswapLoc.T("about.maintained_by"), VRCWorldHotswap.MaintainerName, null, VRCWorldHotswap.MaintainerUrl);
        DrawLinkedLine(
        VRCWorldHotswapLoc.T("about.based_on_prefix"),
        VRCWorldHotswap.OriginalAuthorName,
        VRCWorldHotswapLoc.T("about.based_on_suffix"),
        VRCWorldHotswap.OriginalAuthorUrl);

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("about.needs_sdk"), EditorStyles.miniLabel);
        if (GUILayout.Button(VRCWorldHotswapLoc.T("btn.close"), GUILayout.Height(28)))
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
