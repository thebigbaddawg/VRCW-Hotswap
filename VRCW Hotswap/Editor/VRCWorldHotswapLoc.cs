#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public enum VRCWLocLang
{
    English = 0,
    ChineseSimplified = 1,
    Japanese = 2,
    Korean = 3,
    Spanish = 4
}

public static class VRCWorldHotswapLoc
{
    private const string PrefsLanguageKey = "VRCWorldHotswap.Language";
    private const string PrefsLanguageAskedKey = "VRCWorldHotswap.LanguageAsked";
    private const string MenuEnglish = "VRCW Hotswap/Language/English";
    private const string MenuChineseSimplified = "VRCW Hotswap/Language/中文 (简体)";
    private const string MenuJapanese = "VRCW Hotswap/Language/日本語";
    private const string MenuKorean = "VRCW Hotswap/Language/한국어";
    private const string MenuSpanish = "VRCW Hotswap/Language/Español";

    private static readonly Dictionary<string, string[]> Table = BuildTable();

    public static VRCWLocLang Current
    {
        get
        {
            int raw = EditorPrefs.GetInt(PrefsLanguageKey, (int)VRCWLocLang.English);
            if (!Enum.IsDefined(typeof(VRCWLocLang), raw))
                raw = (int)VRCWLocLang.English;
            return (VRCWLocLang)raw;
        }
    }

    public static void SetLanguage(VRCWLocLang language)
    {
        EditorPrefs.SetInt(PrefsLanguageKey, (int)language);
    }

    public static string NativeName(VRCWLocLang language)
    {
        switch (language)
        {
            case VRCWLocLang.ChineseSimplified: return "中文 (简体)";
            case VRCWLocLang.Japanese: return "日本語";
            case VRCWLocLang.Korean: return "한국어";
            case VRCWLocLang.Spanish: return "Español";
            default: return "English";
        }
    }

    // First run only. Closing the picker leaves English, which is also the pref default.
    public static void PromptFirstRunLanguageIfNeeded()
    {
        if (Application.isBatchMode)
            return;

        if (EditorPrefs.GetBool(PrefsLanguageAskedKey, false) || EditorPrefs.HasKey(PrefsLanguageKey))
            return;

        EditorPrefs.SetBool(PrefsLanguageAskedKey, true);

        VRCWLocLang? picked = VRCWorldHotswapLanguagePicker.Prompt();
        SetLanguage(picked ?? VRCWLocLang.English);
    }

    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key))
            return "";

        if (!Table.TryGetValue(key, out string[] values) || values == null || values.Length == 0)
            return key;

        int index = (int)Current;
        if (index < 0)
            index = 0;

        if (index >= values.Length || string.IsNullOrEmpty(values[index]))
            index = 0;

        return string.IsNullOrEmpty(values[index]) ? key : values[index];
    }

    public static string TF(string key, params object[] args)
    {
        string template = T(key);
        if (args == null || args.Length == 0)
            return template;

        return string.Format(CultureInfo.InvariantCulture, template, args);
    }

    [MenuItem(MenuEnglish, false, 100)]
    private static void SetEnglish()
    {
        ChangeLanguage(VRCWLocLang.English);
    }

    [MenuItem(MenuChineseSimplified, false, 101)]
    private static void SetChineseSimplified()
    {
        ChangeLanguage(VRCWLocLang.ChineseSimplified);
    }

    [MenuItem(MenuJapanese, false, 102)]
    private static void SetJapanese()
    {
        ChangeLanguage(VRCWLocLang.Japanese);
    }

    [MenuItem(MenuKorean, false, 103)]
    private static void SetKorean()
    {
        ChangeLanguage(VRCWLocLang.Korean);
    }

    [MenuItem(MenuSpanish, false, 104)]
    private static void SetSpanish()
    {
        ChangeLanguage(VRCWLocLang.Spanish);
    }

    [MenuItem(MenuEnglish, true)]
    private static bool ValidateEnglish()
    {
        Menu.SetChecked(MenuEnglish, Current == VRCWLocLang.English);
        return true;
    }

    [MenuItem(MenuChineseSimplified, true)]
    private static bool ValidateChineseSimplified()
    {
        Menu.SetChecked(MenuChineseSimplified, Current == VRCWLocLang.ChineseSimplified);
        return true;
    }

    [MenuItem(MenuJapanese, true)]
    private static bool ValidateJapanese()
    {
        Menu.SetChecked(MenuJapanese, Current == VRCWLocLang.Japanese);
        return true;
    }

    [MenuItem(MenuKorean, true)]
    private static bool ValidateKorean()
    {
        Menu.SetChecked(MenuKorean, Current == VRCWLocLang.Korean);
        return true;
    }

    [MenuItem(MenuSpanish, true)]
    private static bool ValidateSpanish()
    {
        Menu.SetChecked(MenuSpanish, Current == VRCWLocLang.Spanish);
        return true;
    }

    private static string LanguageNameKey(VRCWLocLang language)
    {
        switch (language)
        {
            case VRCWLocLang.ChineseSimplified: return "lang.zh_hans";
            case VRCWLocLang.Japanese: return "lang.ja";
            case VRCWLocLang.Korean: return "lang.ko";
            case VRCWLocLang.Spanish: return "lang.es";
            default: return "lang.english";
        }
    }

    private static void ChangeLanguage(VRCWLocLang language)
    {
        if (Current == language)
            return;

        SetLanguage(language);
        EditorUtility.DisplayDialog(
            T("app.name"),
            TF("lang.changed", T(LanguageNameKey(language))),
            T("btn.ok"));
    }

    private static Dictionary<string, string[]> BuildTable()
    {
        var map = new Dictionary<string, string[]>();

        void Add(string key, string en, string zh, string ja, string ko, string es)
        {
            map[key] = new[]
            {
                en,
                string.IsNullOrEmpty(zh) ? en : zh,
                string.IsNullOrEmpty(ja) ? en : ja,
                string.IsNullOrEmpty(ko) ? en : ko,
                string.IsNullOrEmpty(es) ? en : es
            };
        }

        Add("app.name", "VRCW Hotswap", "VRCW Hotswap", "VRCW Hotswap", "VRCW Hotswap", "VRCW Hotswap");
        Add("app.name.android",
            "VRCW Hotswap - Android",
            "VRCW Hotswap - Android",
            "VRCW Hotswap - Android",
            "VRCW Hotswap - Android",
            "VRCW Hotswap - Android");
        Add("app.name.packing",
            "VRCW Hotswap - Packing",
            "VRCW Hotswap - 打包",
            "VRCW Hotswap - パック",
            "VRCW Hotswap - 패킹",
            "VRCW Hotswap - Empaquetado");
        Add("app.name.lzma", "VRCW Hotswap - LZMA", "VRCW Hotswap - LZMA", "VRCW Hotswap - LZMA", "VRCW Hotswap - LZMA", "VRCW Hotswap - LZMA");
        Add("app.name.sdk_problem",
            "VRCW Hotswap - SDK problem?",
            "VRCW Hotswap - SDK 问题？",
            "VRCW Hotswap - SDK の問題？",
            "VRCW Hotswap - SDK 문제?",
            "VRCW Hotswap - ¿Problema con el SDK?");
        Add("app.name.unity_version",
            "VRCW Hotswap - Unity version?",
            "VRCW Hotswap - Unity 版本？",
            "VRCW Hotswap - Unity のバージョン？",
            "VRCW Hotswap - Unity 버전?",
            "VRCW Hotswap - ¿Versión de Unity?");
        Add("app.name.dwr_bundle",
            "VRCW Hotswap - DWR bundle",
            "VRCW Hotswap - DWR 包",
            "VRCW Hotswap - DWR バンドル",
            "VRCW Hotswap - DWR 번들",
            "VRCW Hotswap - Bundle DWR");
        Add("app.name.unity_mismatch",
            "VRCW Hotswap - Unity version mismatch",
            "VRCW Hotswap - Unity 版本不匹配",
            "VRCW Hotswap - Unity バージョン不一致",
            "VRCW Hotswap - Unity 버전 불일치",
            "VRCW Hotswap - Versión de Unity distinta");
        Add("app.name.platform",
            "VRCW Hotswap - Platform?",
            "VRCW Hotswap - 平台？",
            "VRCW Hotswap - プラットフォーム？",
            "VRCW Hotswap - 플랫폼?",
            "VRCW Hotswap - ¿Plataforma?");
        Add("app.name.wrong_platform",
            "VRCW Hotswap - Wrong platform?",
            "VRCW Hotswap - 平台不匹配？",
            "VRCW Hotswap - プラットフォーム違い？",
            "VRCW Hotswap - 플랫폼 불일치?",
            "VRCW Hotswap - ¿Plataforma incorrecta?");
        Add("app.name.pc_size",
            "VRCW Hotswap - PC size",
            "VRCW Hotswap - PC 大小",
            "VRCW Hotswap - PC のサイズ",
            "VRCW Hotswap - PC 용량",
            "VRCW Hotswap - Tamaño en PC");
        Add("app.name.android_size",
            "VRCW Hotswap - Android size",
            "VRCW Hotswap - Android 大小",
            "VRCW Hotswap - Android のサイズ",
            "VRCW Hotswap - Android 용량",
            "VRCW Hotswap - Tamaño en Android");
        Add("inspect.title", "Inspect World File", "检查世界文件", "ワールドファイルを確認", "월드 파일 확인", "Inspeccionar archivo de mundo");
        Add("about.title", "About VRCW Hotswap", "关于 VRCW Hotswap", "VRCW Hotswap について", "VRCW Hotswap 정보", "Acerca de VRCW Hotswap");

        Add("lang.english", "English", "英文", "英語", "영어", "Inglés");
        Add("lang.zh_hans", "Chinese (Simplified)", "简体中文", "中国語（簡体字）", "중국어(간체)", "Chino (simplificado)");
        Add("lang.ja", "Japanese", "日语", "日本語", "일본어", "Japonés");
        Add("lang.ko", "Korean", "韩语", "韓国語", "한국어", "Coreano");
        Add("lang.es", "Spanish", "西班牙语", "スペイン語", "스페인어", "Español");
        Add("lang.changed",
            "Language switched to {0}.",
            "语言已切换为{0}。",
            "言語を{0}に切り替えました。",
            "표시 언어를 {0}로 변경했습니다.",
            "Idioma cambiado a {0}.");

        // Shown before a language is chosen, so every column is deliberately multilingual.
        string pickerTitle = "VRCW Hotswap - Language / 语言 / 言語 / 언어 / Idioma";
        string pickerBody =
            "Choose your display language for VRCW Hotswap.\n" +
            "请选择 VRCW Hotswap 的显示语言。\n" +
            "VRCW Hotswap の表示言語を選んでください。\n" +
            "VRCW Hotswap에서 사용할 언어를 선택하세요.\n" +
            "Elige el idioma de VRCW Hotswap.";
        string pickerFooter =
            "Menu names stay in English. You can change this any time in VRCW Hotswap > Language.\n" +
            "菜单名称保持英文。之后可以随时在 VRCW Hotswap > Language 里更改。\n" +
            "メニュー名は英語のままです。VRCW Hotswap > Language でいつでも変更できます。\n" +
            "메뉴 이름은 영어로 유지됩니다. VRCW Hotswap > Language에서 언제든지 변경할 수 있습니다.\n" +
            "Los nombres de los menús se quedan en inglés. Puedes cambiarlo cuando quieras en VRCW Hotswap > Language.";
        Add("lang.picker.title", pickerTitle, pickerTitle, pickerTitle, pickerTitle, pickerTitle);
        Add("lang.picker.body", pickerBody, pickerBody, pickerBody, pickerBody, pickerBody);
        Add("lang.picker.footer", pickerFooter, pickerFooter, pickerFooter, pickerFooter, pickerFooter);

        Add("platform.pc", "PC", "PC", "PC", "PC", "PC");
        Add("platform.android", "Android", "Android", "Android", "Android", "Android");
        Add("action.hotswap", "Hotswap", "热替换", "ホットスワップ", "핫스왑", "hotswap");
        Add("action.upload", "Upload", "上传", "アップロード", "업로드", "subida");
        Add("action.inspect", "Inspect", "检查", "確認", "확인", "inspección");

        Add("btn.ok", "Ok", "确定", "OK", "확인", "Aceptar");
        Add("btn.cancel", "Cancel", "取消", "キャンセル", "취소", "Cancelar");
        Add("btn.continue", "Continue", "继续", "続行", "계속", "Continuar");
        Add("btn.continue_anyway", "Continue Anyway", "仍然继续", "それでも続行", "무시하고 계속", "Continuar igualmente");
        Add("btn.load_new_file", "Load New File", "加载新文件", "別のファイルを読み込む", "다른 파일 불러오기", "Cargar otro archivo");
        Add("btn.upload", "Upload", "上传", "アップロード", "업로드", "Subir");
        Add("btn.lz4runtime", "LZ4Runtime", "LZ4Runtime", "LZ4Runtime", "LZ4Runtime", "LZ4Runtime");
        Add("btn.lzma", "LZMA", "LZMA", "LZMA", "LZMA", "LZMA");
        Add("btn.pick_id", "Pick ID", "选择 ID", "ID を選ぶ", "ID 선택", "Elegir ID");
        Add("btn.use_first", "Use First", "使用第一个", "最初のものを使う", "첫 번째 사용", "Usar el primero");
        Add("btn.try_anyway", "Try anyway", "仍要尝试", "それでも試す", "그래도 시도", "Intentarlo igualmente");
        Add("btn.try_packing", "Try Packing", "尝试打包", "パックしてみる", "패킹 시도", "Probar a empaquetar");
        Add("btn.i_understand_likely_fail",
            "I understand it will likely fail",
            "我知道大概率会失败",
            "失敗覚悟で続行",
            "실패를 감안하고 계속",
            "Sé que probablemente falle");
        Add("btn.reset", "Reset", "重置", "リセット", "초기화", "Reiniciar");
        Add("btn.cancel_and_reset", "Cancel & Reset", "取消并重置", "キャンセルしてリセット", "취소하고 초기화", "Cancelar y reiniciar");
        Add("btn.keep_going", "Keep Going", "保持不变", "そのままにする", "그대로 두기", "Dejarlo como está");
        Add("btn.use_selected", "Use Selected", "使用所选项", "選択したものを使う", "선택한 항목 사용", "Usar lo seleccionado");
        Add("btn.close", "Close", "关闭", "閉じる", "닫기", "Cerrar");
        Add("btn.show_howto_again",
            "Show howto again",
            "再次显示使用说明",
            "使い方をもう一度表示",
            "사용법 다시 보기",
            "Ver las instrucciones otra vez");

        Add("filter.world_files", "World Files", "世界文件", "ワールドファイル", "월드 파일", "Archivos de mundo");
        Add("filter.all_files", "All files", "所有文件", "すべてのファイル", "모든 파일", "Todos los archivos");

        Add("howto.body",
            "How to use:\n\n" +
            "1) Open a simple world scene\n" +
            "   (or VRCW Hotswap > Spawn Dummy World)\n\n" +
            "2) In the VRChat SDK, click Build & Publish once\n" +
            "   (this sets up your world ID and build file)\n\n" +
            "3) VRCW Hotswap > Load Hotswap File (.vrcw)\n" +
            "   and pick the world you wish to hotswap\n\n" +
            "4) VRCW Hotswap > Upload Hotswapped Build\n\n" +
            "After step 3, do NOT click Build & Publish again.\n" +
            "That rebuilds the scene and undoes the swap.\n\n" +
            "Your original .vrcw file is left untouched.",
            "使用方法：\n\n" +
            "1) 打开一个简单的世界场景\n" +
            "   （或使用 VRCW Hotswap > Spawn Dummy World 生成占位世界）\n\n" +
            "2) 在 VRChat SDK 里点击一次 Build & Publish（构建并发布）\n" +
            "   （这一步会生成 world ID 和构建文件）\n\n" +
            "3) 使用 VRCW Hotswap > Load Hotswap File（加载热替换文件）\n" +
            "   选择你要热替换的 .vrcw\n\n" +
            "4) 使用 VRCW Hotswap > Upload Hotswapped Build（上传热替换构建）\n\n" +
            "做完第 3 步后，不要再点 Build & Publish，\n" +
            "否则会重新构建场景，热替换就白做了。\n\n" +
            "原始 .vrcw 文件不会被改动。",
            "使い方：\n\n" +
            "1) シンプルなワールドシーンを開く\n" +
            "   （または VRCW Hotswap > Spawn Dummy World でダミーワールドを生成）\n\n" +
            "2) VRChat SDK で Build & Publish（ビルドと公開）を 1 回押す\n" +
            "   （これで world ID とビルドファイルが用意されます）\n\n" +
            "3) VRCW Hotswap > Load Hotswap File（ホットスワップファイルを読み込み）で\n" +
            "   入れ替えたい .vrcw を選ぶ\n\n" +
            "4) VRCW Hotswap > Upload Hotswapped Build（ホットスワップ後のビルドをアップロード）\n\n" +
            "手順 3 のあとは Build & Publish を押さないでください。\n" +
            "押すとシーンが再ビルドされ、入れ替えが元に戻ってしまいます。\n\n" +
            "元の .vrcw ファイルは変更されません。",
            "사용법:\n\n" +
            "1) 간단한 월드 씬을 엽니다\n" +
            "   (또는 VRCW Hotswap > Spawn Dummy World(더미 월드 생성) 사용)\n\n" +
            "2) VRChat SDK에서 Build & Publish(빌드 및 게시)를 한 번 누릅니다\n" +
            "   (이 단계에서 world ID와 빌드 파일이 준비됩니다)\n\n" +
            "3) VRCW Hotswap > Load Hotswap File(핫스왑 파일 불러오기)에서\n" +
            "   교체할 .vrcw를 선택합니다\n\n" +
            "4) VRCW Hotswap > Upload Hotswapped Build(핫스왑된 빌드 업로드)\n\n" +
            "3단계 이후에는 Build & Publish를 다시 누르지 마세요.\n" +
            "누르면 씬이 다시 빌드되어 교체가 취소됩니다.\n\n" +
            "원본 .vrcw 파일은 변경되지 않습니다.",
            "Cómo se usa:\n\n" +
            "1) Abre una escena de mundo sencilla\n" +
            "   (o usa VRCW Hotswap > Spawn Dummy World (crear mundo de prueba))\n\n" +
            "2) En el VRChat SDK, haz clic una vez en Build & Publish (compilar y publicar)\n" +
            "   (así se preparan tu world ID y el archivo de la build)\n\n" +
            "3) VRCW Hotswap > Load Hotswap File (cargar archivo de hotswap)\n" +
            "   y elige el .vrcw que quieres intercambiar\n\n" +
            "4) VRCW Hotswap > Upload Hotswapped Build (subir la build con hotswap)\n\n" +
            "Después del paso 3, NO vuelvas a hacer clic en Build & Publish.\n" +
            "Eso recompila la escena y deshace el intercambio.\n\n" +
            "Tu archivo .vrcw original no se modifica.");

        Add("dialog.sdk_missing",
            "VRChat World SDK is not in this project.",
            "这个项目里没有 VRChat World SDK。",
            "このプロジェクトに VRChat World SDK がありません。",
            "이 프로젝트에 VRChat World SDK가 없습니다.",
            "Este proyecto no tiene el VRChat World SDK.");
        Add("dialog.already_loaded",
            "You already loaded a world.\n\nLoad a different one instead?\nThis clears the current swap first.",
            "你已经加载过一个世界了。\n\n要换成加载另一个吗？\n这会先清除当前的热替换。",
            "すでにワールドを読み込んでいます。\n\n別のワールドを読み込みますか？\n先に現在のホットスワップをクリアします。",
            "이미 월드를 불러왔습니다.\n\n다른 월드를 불러올까요?\n먼저 현재 핫스왑을 지웁니다.",
            "Ya has cargado un mundo.\n\n¿Quieres cargar otro?\nPrimero se borra el hotswap actual.");
        Add("dialog.howto_continue",
            "{0}\n\nContinue?",
            "{0}\n\n继续吗？",
            "{0}\n\n続けますか？",
            "{0}\n\n계속할까요?",
            "{0}\n\n¿Continuar?");
        Add("dialog.scene_no_world_setup",
            "This scene has no world setup.\n\nUse Spawn Dummy World, or open a world scene.",
            "当前场景里没有世界设置。\n\n请使用 Spawn Dummy World（生成占位世界），或打开一个世界场景。",
            "このシーンにはワールドの設定がありません。\n\nSpawn Dummy World（ダミーワールドを生成）を使うか、ワールドシーンを開いてください。",
            "이 씬에는 월드 설정이 없습니다.\n\nSpawn Dummy World(더미 월드 생성)를 사용하거나 월드 씬을 열어주세요.",
            "Esta escena no tiene configuración de mundo.\n\nUsa Spawn Dummy World (crear mundo de prueba) o abre una escena de mundo.");
        Add("dialog.scene_no_world_id",
            "This scene has no world ID yet.\n\nClick Build & Publish in the VRChat SDK first, then try again.",
            "当前场景还没有 world ID。\n\n请先在 VRChat SDK 里点击 Build & Publish（构建并发布），然后再试一次。",
            "このシーンにはまだ world ID がありません。\n\n先に VRChat SDK で Build & Publish（ビルドと公開）を押してから、もう一度試してください。",
            "이 씬에는 아직 world ID가 없습니다.\n\n먼저 VRChat SDK에서 Build & Publish(빌드 및 게시)를 누른 뒤 다시 시도하세요.",
            "Esta escena todavía no tiene world ID.\n\nHaz clic primero en Build & Publish (compilar y publicar) en el VRChat SDK y vuelve a intentarlo.");
        Add("dialog.bad_world_id",
            "Bad world ID on this scene:\n{0}",
            "当前场景的 world ID 无效：\n{0}",
            "このシーンの world ID が正しくありません：\n{0}",
            "이 씬의 world ID가 올바르지 않습니다:\n{0}",
            "El world ID de esta escena no es válido:\n{0}");
        Add("dialog.no_sdk_build_continue",
            "No SDK build found yet.\n\nClick Build & Publish in the VRChat SDK first.\n\nContinue anyway? You can still save a file by hand.",
            "还没有找到 SDK 的构建文件。\n\n请先在 VRChat SDK 里点击 Build & Publish（构建并发布）。\n\n仍然继续吗？继续的话你还可以手动把文件另存出来。",
            "SDK のビルドファイルがまだ見つかりません。\n\n先に VRChat SDK で Build & Publish（ビルドと公開）を押してください。\n\nそれでも続けますか？続ける場合も、ファイルを手動で保存できます。",
            "SDK 빌드 파일을 아직 찾지 못했습니다.\n\n먼저 VRChat SDK에서 Build & Publish(빌드 및 게시)를 누르세요.\n\n그래도 계속할까요? 계속하면 파일을 직접 저장할 수 있습니다.",
            "Todavía no se encuentra ninguna build del SDK.\n\nHaz clic primero en Build & Publish (compilar y publicar) en el VRChat SDK.\n\n¿Continuar igualmente? Aún puedes guardar el archivo a mano.");
        Add("dialog.pick_hotswap_file",
            "Pick the .vrcw to hotswap",
            "选择要热替换的 .vrcw",
            "ホットスワップする .vrcw を選択",
            "핫스왑할 .vrcw 선택",
            "Elige el .vrcw para el hotswap");
        Add("dialog.busy",
            "Something else is already running.\n\nWait for it to finish, then try again.",
            "已经有其他操作在运行了。\n\n等它跑完再试一次。",
            "ほかの処理が実行中です。\n\n終わるまで待ってから、もう一度試してください。",
            "다른 작업이 실행 중입니다.\n\n끝날 때까지 기다린 뒤 다시 시도하세요.",
            "Ya hay otra cosa en marcha.\n\nEspera a que termine y vuelve a intentarlo.");
        Add("dialog.nothing_ready_upload",
            "Nothing ready to upload.\n\nLoad a .vrcw first (Load Hotswap File).",
            "现在没有可上传的东西。\n\n请先用 Load Hotswap File（加载热替换文件）载入一个 .vrcw。",
            "アップロードできるものがありません。\n\n先に Load Hotswap File（ホットスワップファイルを読み込み）で .vrcw を読み込んでください。",
            "업로드할 것이 없습니다.\n\n먼저 Load Hotswap File(핫스왑 파일 불러오기)로 .vrcw를 불러오세요.",
            "No hay nada listo para subir.\n\nCarga primero un .vrcw con Load Hotswap File (cargar archivo de hotswap).");
        Add("dialog.nothing_ready_upload_retry",
            "Nothing ready to upload.\n\nLoad a .vrcw first, then try again.",
            "现在没有可上传的东西。\n\n请先加载一个 .vrcw，然后再试一次。",
            "アップロードできるものがありません。\n\n先に .vrcw を読み込んでから、もう一度試してください。",
            "업로드할 것이 없습니다.\n\n먼저 .vrcw를 불러온 뒤 다시 시도하세요.",
            "No hay nada listo para subir.\n\nCarga primero un .vrcw y vuelve a intentarlo.");
        Add("dialog.open_sdk_control_panel",
            "Open the VRChat SDK Control Panel.\n\n" +
            "Sign in, go to Builder, fill in name / description / image, then try Upload again.\n\n" +
            "Don't click Build & Publish after a hotswap.",
            "请打开 VRChat SDK 的 Control Panel（控制面板）。\n\n" +
            "登录后进入 Builder（构建面板），填好名称 / 描述 / 图片，再重新点一次 Upload（上传）。\n\n" +
            "热替换之后不要再点 Build & Publish（构建并发布）。",
            "VRChat SDK の Control Panel（コントロールパネル）を開いてください。\n\n" +
            "ログインして Builder（ビルダー）に移動し、名前 / 説明 / 画像を入力してから、もう一度 Upload（アップロード）を押してください。\n\n" +
            "ホットスワップのあとに Build & Publish（ビルドと公開）は押さないでください。",
            "VRChat SDK의 Control Panel(컨트롤 패널)을 열어주세요.\n\n" +
            "로그인한 뒤 Builder(빌더)로 이동해 이름 / 설명 / 이미지를 입력하고, Upload(업로드)를 다시 누르세요.\n\n" +
            "핫스왑 후에는 Build & Publish(빌드 및 게시)를 누르지 마세요.",
            "Abre el Control Panel (panel de control) del VRChat SDK.\n\n" +
            "Inicia sesión, ve a Builder (constructor), rellena nombre / descripción / imagen y vuelve a pulsar Upload (subir).\n\n" +
            "Después de un hotswap no hagas clic en Build & Publish (compilar y publicar).");
        Add("dialog.vrchat_uploading",
            "VRChat is already uploading something.\n\nWait, then try again.",
            "VRChat 那边已经有一个上传在进行了。\n\n等它结束后再试。",
            "VRChat 側で別のアップロードが進行中です。\n\n終わってから、もう一度試してください。",
            "VRChat에서 이미 다른 업로드가 진행 중입니다.\n\n끝난 뒤 다시 시도하세요.",
            "VRChat ya está subiendo algo.\n\nEspera y vuelve a intentarlo.");
        Add("dialog.scene_needs_world_id",
            "This scene needs a world ID.\n\nClick Build & Publish in the VRChat SDK first.",
            "当前场景还需要一个 world ID。\n\n请先在 VRChat SDK 里点击 Build & Publish（构建并发布）。",
            "このシーンには world ID が必要です。\n\n先に VRChat SDK で Build & Publish（ビルドと公開）を押してください。",
            "이 씬에는 world ID가 필요합니다.\n\n먼저 VRChat SDK에서 Build & Publish(빌드 및 게시)를 누르세요.",
            "Esta escena necesita un world ID.\n\nHaz clic primero en Build & Publish (compilar y publicar) en el VRChat SDK.");
        Add("dialog.world_name_required",
            "Set a world name in the VRChat SDK Builder first.",
            "请先在 VRChat SDK 的 Builder（构建面板）里填写世界名称。",
            "先に VRChat SDK の Builder（ビルダー）でワールド名を入力してください。",
            "먼저 VRChat SDK의 Builder(빌더)에서 월드 이름을 입력하세요.",
            "Pon primero un nombre de mundo en el Builder (constructor) del VRChat SDK.");
        Add("dialog.thumbnail_required",
            "New worlds need a thumbnail.\n\nSet one in the VRChat SDK Builder, then try again.",
            "新世界必须有缩略图。\n\n请先在 VRChat SDK 的 Builder（构建面板）里设置一张，然后再试。",
            "新規ワールドにはサムネイルが必要です。\n\nVRChat SDK の Builder（ビルダー）で設定してから、もう一度試してください。",
            "새 월드에는 썸네일이 필요합니다.\n\nVRChat SDK의 Builder(빌더)에서 설정한 뒤 다시 시도하세요.",
            "Los mundos nuevos necesitan una miniatura.\n\nPon una en el Builder (constructor) del VRChat SDK y vuelve a intentarlo.");
        Add("dialog.upload_confirm",
            "Upload now?\n\n" +
            "Platform: {0}\n" +
            "Size: {1}\n" +
            "Name: {2}\n" +
            "World ID: {3}\n" +
            "{4}\n\n" +
            "Tip: after this, don't click Build & Publish.",
            "现在上传吗？\n\n" +
            "平台：{0}\n" +
            "大小：{1}\n" +
            "名称：{2}\n" +
            "World ID：{3}\n" +
            "{4}\n\n" +
            "提示：上传完之后不要再点 Build & Publish（构建并发布）。",
            "今すぐアップロードしますか？\n\n" +
            "プラットフォーム：{0}\n" +
            "サイズ：{1}\n" +
            "名前：{2}\n" +
            "World ID：{3}\n" +
            "{4}\n\n" +
            "ヒント：アップロードのあとに Build & Publish（ビルドと公開）は押さないでください。",
            "지금 업로드할까요?\n\n" +
            "플랫폼: {0}\n" +
            "용량: {1}\n" +
            "이름: {2}\n" +
            "World ID: {3}\n" +
            "{4}\n\n" +
            "팁: 업로드 후에는 Build & Publish(빌드 및 게시)를 누르지 마세요.",
            "¿Subir ahora?\n\n" +
            "Plataforma: {0}\n" +
            "Tamaño: {1}\n" +
            "Nombre: {2}\n" +
            "World ID: {3}\n" +
            "{4}\n\n" +
            "Consejo: después de esto, no hagas clic en Build & Publish (compilar y publicar).");
        Add("dialog.upload_confirm_creates",
            "Creates a new world.",
            "这会创建一个新世界。",
            "新しいワールドを作成します。",
            "새 월드를 만듭니다.",
            "Se creará un mundo nuevo.");
        Add("dialog.upload_confirm_updates",
            "Updates your existing world.",
            "这会更新你已有的世界。",
            "既存のワールドを更新します。",
            "기존 월드를 업데이트합니다.",
            "Se actualizará tu mundo actual.");
        Add("dialog.upload_cancelled",
            "Upload cancelled.",
            "上传已取消。",
            "アップロードをキャンセルしました。",
            "업로드를 취소했습니다.",
            "Subida cancelada.");
        Add("dialog.upload_finished",
            "Upload finished.\n\nCheck the VRChat SDK panel if anything looks wrong.",
            "上传完成。\n\n如果哪里看起来不对，去 VRChat SDK 的 Control Panel（控制面板）确认一下。",
            "アップロードが完了しました。\n\nおかしいところがあれば VRChat SDK の Control Panel（コントロールパネル）を確認してください。",
            "업로드가 완료되었습니다.\n\n이상한 점이 있으면 VRChat SDK의 Control Panel(컨트롤 패널)을 확인하세요.",
            "Subida terminada.\n\nSi algo no cuadra, revisa el Control Panel (panel de control) del VRChat SDK.");
        Add("dialog.upload_failed",
            "Upload failed:\n{0}\n\nSee the Console for more info.",
            "上传失败：\n{0}\n\n详细信息见 Console（控制台）。",
            "アップロードに失敗しました：\n{0}\n\n詳細は Console（コンソール）を確認してください。",
            "업로드에 실패했습니다:\n{0}\n\n자세한 내용은 Console(콘솔)을 확인하세요.",
            "La subida falló:\n{0}\n\nMira la Console (consola) para más detalles.");

        Add("dialog.sdk_problem.body",
            "Can't talk to the VRChat SDK the way this tool expects.\n\n" +
            "Often this means the Worlds SDK updated and broke this tool.\n\n" +
            "Tested with:\n" +
            "• SDK {0} / Unity {1}\n" +
            "• SDK {2} / Unity {3}\n" +
            "• SDK {4} / Unity {5}\n" +
            "• Partially tested: SDK {4} / Unity {5} with 22f2-DWR bundles\n" +
            "Your Unity: {6}\n" +
            "Your SDK (guess): {7}\n\n" +
            "Try: open VRChat SDK > Builder, sign in, fill name / image, then retry.\n" +
            "If you just updated the SDK, note your versions and check the Console.\n\n" +
            "Detail:\n{8}",
            "这个工具没能按预期调用 VRChat SDK。\n\n" +
            "一般是 Worlds SDK 更新后把这个工具弄坏了。\n\n" +
            "已测试的版本：\n" +
            "• SDK {0} / Unity {1}\n" +
            "• SDK {2} / Unity {3}\n" +
            "• SDK {4} / Unity {5}\n" +
            "• 部分测试：SDK {4} / Unity {5}，22f2-DWR 包\n" +
            "你的 Unity：{6}\n" +
            "你的 SDK（推测）：{7}\n\n" +
            "可以试试：打开 VRChat SDK > Builder（构建面板），登录，填好名称 / 图片，然后重试。\n" +
            "如果你刚更新过 SDK，记下版本号并看一下 Console（控制台）。\n\n" +
            "详细信息：\n{8}",
            "このツールが想定している方法で VRChat SDK を呼び出せませんでした。\n\n" +
            "多くの場合、Worlds SDK の更新でこのツールが動かなくなったのが原因です。\n\n" +
            "動作確認済みのバージョン：\n" +
            "• SDK {0} / Unity {1}\n" +
            "• SDK {2} / Unity {3}\n" +
            "• SDK {4} / Unity {5}\n" +
            "• 一部のみ確認：SDK {4} / Unity {5}、22f2-DWR バンドル\n" +
            "お使いの Unity：{6}\n" +
            "お使いの SDK（推定）：{7}\n\n" +
            "対処：VRChat SDK > Builder（ビルダー）を開き、ログインして名前 / 画像を入力してから、もう一度試してください。\n" +
            "SDK を更新した直後なら、バージョンを記録して Console（コンソール）を確認してください。\n\n" +
            "詳細：\n{8}",
            "이 도구가 기대하는 방식으로 VRChat SDK를 호출할 수 없습니다.\n\n" +
            "보통 Worlds SDK가 업데이트되면서 이 도구가 동작하지 않게 된 경우입니다.\n\n" +
            "동작이 확인된 버전:\n" +
            "• SDK {0} / Unity {1}\n" +
            "• SDK {2} / Unity {3}\n" +
            "• SDK {4} / Unity {5}\n" +
            "• 일부만 확인: SDK {4} / Unity {5}, 22f2-DWR 번들\n" +
            "사용 중인 Unity: {6}\n" +
            "사용 중인 SDK(추정): {7}\n\n" +
            "해결 방법: VRChat SDK > Builder(빌더)를 열고 로그인해 이름 / 이미지를 입력한 뒤 다시 시도하세요.\n" +
            "SDK를 방금 업데이트했다면 버전을 기록하고 Console(콘솔)을 확인하세요.\n\n" +
            "자세한 내용:\n{8}",
            "No se puede hablar con el VRChat SDK como espera esta herramienta.\n\n" +
            "Normalmente significa que el Worlds SDK se actualizó y rompió esta herramienta.\n\n" +
            "Probado con:\n" +
            "• SDK {0} / Unity {1}\n" +
            "• SDK {2} / Unity {3}\n" +
            "• SDK {4} / Unity {5}\n" +
            "• Probado en parte: SDK {4} / Unity {5} con bundles 22f2-DWR\n" +
            "Tu Unity: {6}\n" +
            "Tu SDK (estimado): {7}\n\n" +
            "Prueba esto: abre VRChat SDK > Builder (constructor), inicia sesión, rellena nombre / imagen y reinténtalo.\n" +
            "Si acabas de actualizar el SDK, anota las versiones y mira la Console (consola).\n\n" +
            "Detalle:\n{8}");

        Add("dialog.select_vrcw_to_inspect",
            "Select a .vrcw to inspect",
            "选择一个要检查的 .vrcw",
            "確認する .vrcw を選択",
            "확인할 .vrcw 선택",
            "Elige un .vrcw para inspeccionar");
        Add("dialog.inspect_failed_read",
            "Couldn't read that .vrcw.\n\nCheck the Console, then try again.",
            "读不了这个 .vrcw。\n\n看一下 Console（控制台），然后再试一次。",
            "この .vrcw を読み取れませんでした。\n\nConsole（コンソール）を確認して、もう一度試してください。",
            "이 .vrcw를 읽을 수 없습니다.\n\nConsole(콘솔)을 확인한 뒤 다시 시도하세요.",
            "No se pudo leer ese .vrcw.\n\nMira la Console (consola) y vuelve a intentarlo.");
        Add("dialog.inspect_failed",
            "Inspect failed:\n{0}",
            "检查失败：\n{0}",
            "確認に失敗しました：\n{0}",
            "확인에 실패했습니다:\n{0}",
            "La inspección falló:\n{0}");
        Add("dialog.scene_has_world_setup",
            "This scene already has a world setup.\nSelected it.",
            "当前场景已经有世界设置了。\n已经帮你选中。",
            "このシーンにはすでにワールドの設定があります。\n選択しました。",
            "이 씬에는 이미 월드 설정이 있습니다.\n해당 오브젝트를 선택했습니다.",
            "Esta escena ya tiene configuración de mundo.\nLa he seleccionado.");
        Add("dialog.no_world_ids",
            "No world IDs found in that .vrcw.\n\nThis file may not be a valid world bundle.",
            "这个 .vrcw 里找不到 world ID。\n\n它可能不是一个有效的世界 bundle。",
            "この .vrcw の中に world ID が見つかりません。\n\n有効なワールドバンドルではない可能性があります。",
            "이 .vrcw에서 world ID를 찾지 못했습니다.\n\n올바른 월드 번들이 아닐 수 있습니다.",
            "No se encontró ningún world ID en ese .vrcw.\n\nPuede que no sea un bundle de mundo válido.");
        Add("dialog.cancelled_no_world_id",
            "Cancelled.\n\nNo world ID was selected.",
            "已取消。\n\n没有选择 world ID。",
            "キャンセルしました。\n\nworld ID が選択されていません。",
            "취소했습니다.\n\nworld ID를 선택하지 않았습니다.",
            "Cancelado.\n\nNo se seleccionó ningún world ID.");
        Add("dialog.same_world_id",
            "This file already uses the same world ID as your scene.\nNothing to change for the ID.",
            "这个文件用的 world ID 和你场景里的一样。\nID 不需要改。",
            "このファイルはシーンと同じ world ID を使っています。\nID の変更は必要ありません。",
            "이 파일은 씬과 같은 world ID를 사용합니다.\nID는 바꿀 필요가 없습니다.",
            "Este archivo ya usa el mismo world ID que tu escena.\nNo hay que cambiar el ID.");
        Add("dialog.world_id_length_mismatch",
            "Can't swap these world IDs.\n\nFile ID length: {0}\nScene ID length: {1}\n\nThey must be the same length.",
            "这两个 world ID 换不了。\n\n文件 ID 长度：{0}\n场景 ID 长度：{1}\n\n两者长度必须相同。",
            "この 2 つの world ID は入れ替えられません。\n\nファイル側の ID の長さ：{0}\nシーン側の ID の長さ：{1}\n\n長さが同じである必要があります。",
            "이 두 world ID는 교체할 수 없습니다.\n\n파일 ID 길이: {0}\n씬 ID 길이: {1}\n\n길이가 같아야 합니다.",
            "No se pueden intercambiar estos world ID.\n\nLongitud del ID del archivo: {0}\nLongitud del ID de la escena: {1}\n\nTienen que medir lo mismo.");
        Add("dialog.multiple_world_ids",
            "This file has {0} world IDs.\nExtras are usually portal links.\n\nPick the main world ID?",
            "这个文件里有 {0} 个 world ID。\n多出来的一般是传送门链接。\n\n要手动选主 world ID 吗？",
            "このファイルには world ID が {0} 個あります。\n余分なものは通常ポータルのリンクです。\n\nメインの world ID を選びますか？",
            "이 파일에는 world ID가 {0}개 있습니다.\n남는 것은 보통 포털 링크입니다.\n\n메인 world ID를 선택할까요?",
            "Este archivo tiene {0} world ID.\nLos de sobra suelen ser enlaces de portales.\n\n¿Eliges el world ID principal?");
        Add("dialog.update_ids_failed",
            "Failed while updating world IDs:\n{0}\n\nSee the Console for more info.",
            "更新 world ID 时出错：\n{0}\n\n详细信息见 Console（控制台）。",
            "world ID の更新中にエラーが発生しました：\n{0}\n\n詳細は Console（コンソール）を確認してください。",
            "world ID를 업데이트하는 중 오류가 발생했습니다:\n{0}\n\n자세한 내용은 Console(콘솔)을 확인하세요.",
            "Error al actualizar los world ID:\n{0}\n\nMira la Console (consola) para más detalles.");
        Add("dialog.compressor_missing",
            "AssetsTools compressor not found.\n\n" +
            "Packing with Unity LZ4Runtime (same fallback as older builds).\n\n" +
            "Expected:\nAssets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe",
            "找不到 AssetsTools 压缩器。\n\n" +
            "这次会用 Unity 的 LZ4Runtime 打包（和旧版本一样的回退方式）。\n\n" +
            "它应该在：\nAssets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe",
            "AssetsTools のコンプレッサーが見つかりません。\n\n" +
            "今回は Unity の LZ4Runtime でパックします（旧バージョンと同じフォールバックです）。\n\n" +
            "本来の場所：\nAssets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe",
            "AssetsTools 압축기를 찾을 수 없습니다.\n\n" +
            "이번에는 Unity의 LZ4Runtime으로 패킹합니다(예전 버전과 동일한 대체 방식).\n\n" +
            "원래 위치:\nAssets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe",
            "No se encuentra el compresor de AssetsTools.\n\n" +
            "Se empaquetará con LZ4Runtime de Unity (el mismo respaldo que en versiones anteriores).\n\n" +
            "Debería estar en:\nAssets/VRCW Hotswap/Editor/Compressor/VRCWHotswapCompressor.exe");
        Add("dialog.cancelled_nothing_uploaded",
            "Cancelled.\n\nNothing was uploaded.",
            "已取消。\n\n没有上传任何东西。",
            "キャンセルしました。\n\n何もアップロードしていません。",
            "취소했습니다.\n\n아무것도 업로드하지 않았습니다.",
            "Cancelado.\n\nNo se subió nada.");
        Add("dialog.hotswap_cancelled",
            "Hotswap cancelled.",
            "热替换已取消。",
            "ホットスワップをキャンセルしました。",
            "핫스왑을 취소했습니다.",
            "Hotswap cancelado.");
        Add("dialog.hotswap_cancelled_scanning",
            "Hotswap cancelled while scanning.",
            "已在扫描阶段取消热替换。",
            "スキャン中にホットスワップをキャンセルしました。",
            "스캔 중에 핫스왑을 취소했습니다.",
            "Hotswap cancelado durante el análisis.");
        Add("dialog.hotswap_cancelled_updating_ids",
            "Hotswap cancelled while updating IDs.",
            "已在更新 ID 阶段取消热替换。",
            "ID の更新中にホットスワップをキャンセルしました。",
            "ID를 업데이트하는 중에 핫스왑을 취소했습니다.",
            "Hotswap cancelado al actualizar los ID.");
        Add("dialog.hotswap_cancelled_packing",
            "Hotswap cancelled during packing.",
            "已在打包阶段取消热替换。",
            "パック中にホットスワップをキャンセルしました。",
            "패킹 중에 핫스왑을 취소했습니다.",
            "Hotswap cancelado durante el empaquetado.");
        Add("dialog.hotswap_cancelled_checking",
            "Hotswap cancelled while checking result.",
            "已在检查结果阶段取消热替换。",
            "結果の確認中にホットスワップをキャンセルしました。",
            "결과를 확인하는 중에 핫스왑을 취소했습니다.",
            "Hotswap cancelado al comprobar el resultado.");
        Add("dialog.inspect_cancelled",
            "Inspect cancelled.",
            "检查已取消。",
            "確認をキャンセルしました。",
            "확인을 취소했습니다.",
            "Inspección cancelada.");
        Add("dialog.lzma_disclaimer",
            "LZMA packs hard and can help heavy worlds upload.\n\n" +
            "Join note: after upload, the world may refuse to join for a while.\n" +
            "Waiting and retrying later usually works.\n\n" +
            "Pack with LZMA?",
            "LZMA 的压缩率更高，能让比较大的世界更容易通过上传。\n\n" +
            "关于进入：上传之后，这个世界有一段时间可能进不去。\n" +
            "过一会儿再试通常就能进了。\n\n" +
            "要用 LZMA 打包吗？",
            "LZMA は圧縮率が高く、重いワールドでもアップロードが通りやすくなります。\n\n" +
            "参加についての注意：アップロード直後は、しばらくワールドに入れないことがあります。\n" +
            "少し待ってから入り直せば、たいてい入れます。\n\n" +
            "LZMA でパックしますか？",
            "LZMA는 압축률이 높아 무거운 월드도 업로드가 통과하기 쉬워집니다.\n\n" +
            "입장 관련 참고: 업로드 직후에는 한동안 월드에 들어가지 못할 수 있습니다.\n" +
            "조금 기다린 뒤 다시 시도하면 대개 들어갈 수 있습니다.\n\n" +
            "LZMA로 패킹할까요?",
            "LZMA comprime mucho y ayuda a que los mundos pesados pasen la subida.\n\n" +
            "Sobre entrar: justo después de subirlo, puede que el mundo no te deje entrar durante un rato.\n" +
            "Esperar y volver a intentarlo más tarde suele funcionar.\n\n" +
            "¿Empaquetar con LZMA?");
        Add("dialog.packing_failed",
            "Packing failed.\n\nResult: {0}\n\nNothing was uploaded. Check the Console, then try again.",
            "打包失败。\n\n结果：{0}\n\n没有上传任何东西。看一下 Console（控制台），然后再试一次。",
            "パックに失敗しました。\n\n結果：{0}\n\n何もアップロードしていません。Console（コンソール）を確認して、もう一度試してください。",
            "패킹에 실패했습니다.\n\n결과: {0}\n\n아무것도 업로드하지 않았습니다. Console(콘솔)을 확인한 뒤 다시 시도하세요.",
            "El empaquetado falló.\n\nResultado: {0}\n\nNo se subió nada. Mira la Console (consola) y vuelve a intentarlo.");
        Add("dialog.invalid_packed_file",
            "Packing produced an empty or invalid file.\n\nNothing was uploaded. Try again, or use a different .vrcw.",
            "打包出来的文件是空的或者无效。\n\n没有上传任何东西。请重试，或者换一个 .vrcw。",
            "パック結果が空、または不正なファイルです。\n\n何もアップロードしていません。もう一度試すか、別の .vrcw を使ってください。",
            "패킹 결과가 빈 파일이거나 올바르지 않습니다.\n\n아무것도 업로드하지 않았습니다. 다시 시도하거나 다른 .vrcw를 사용하세요.",
            "El empaquetado dio un archivo vacío o no válido.\n\nNo se subió nada. Vuelve a intentarlo o usa otro .vrcw.");
        Add("dialog.packed_file_open_failed",
            "The packed file couldn't be opened.\n\nNothing was uploaded. Try again, or use a different .vrcw.",
            "打包后的文件打不开。\n\n没有上传任何东西。请重试，或者换一个 .vrcw。",
            "パックしたファイルを開けませんでした。\n\n何もアップロードしていません。もう一度試すか、別の .vrcw を使ってください。",
            "패킹한 파일을 열 수 없습니다.\n\n아무것도 업로드하지 않았습니다. 다시 시도하거나 다른 .vrcw를 사용하세요.",
            "No se pudo abrir el archivo empaquetado.\n\nNo se subió nada. Vuelve a intentarlo o usa otro .vrcw.");
        Add("dialog.packed_file_no_assets",
            "The packed file has no scene/assets.\n\nNothing was uploaded. Try a different .vrcw.",
            "打包后的文件里没有场景或资源。\n\n没有上传任何东西。请换一个 .vrcw。",
            "パックしたファイルにシーンやアセットが入っていません。\n\n何もアップロードしていません。別の .vrcw を使ってください。",
            "패킹한 파일에 씬이나 애셋이 없습니다.\n\n아무것도 업로드하지 않았습니다. 다른 .vrcw를 사용하세요.",
            "El archivo empaquetado no tiene escena ni assets.\n\nNo se subió nada. Usa otro .vrcw.");
        Add("dialog.save_hotswapped_world",
            "Save hotswapped world",
            "保存热替换后的世界文件",
            "ホットスワップ後のワールドファイルを保存",
            "핫스왑된 월드 파일 저장",
            "Guardar el mundo con hotswap");
        Add("dialog.write_failed",
            "Couldn't write the hotswapped file:\n{0}\n\nNothing was uploaded.",
            "热替换后的文件写不进去：\n{0}\n\n没有上传任何东西。",
            "ホットスワップ後のファイルを書き込めませんでした：\n{0}\n\n何もアップロードしていません。",
            "핫스왑된 파일을 쓸 수 없습니다:\n{0}\n\n아무것도 업로드하지 않았습니다.",
            "No se pudo escribir el archivo con hotswap:\n{0}\n\nNo se subió nada.");
        Add("dialog.world_loaded",
            "World loaded!\n\n" +
            "Your original .vrcw file is left untouched.\n\n" +
            "{0}{1}{2}{3}{4}" +
            "World ID: {5}\n\n" +
            "Next: VRCW Hotswap > Upload Hotswapped Build\n" +
            "Don't click Build & Publish after this.",
            "世界已加载！\n\n" +
            "原始 .vrcw 文件没有被改动。\n\n" +
            "{0}{1}{2}{3}{4}" +
            "World ID：{5}\n\n" +
            "下一步：VRCW Hotswap > Upload Hotswapped Build（上传热替换构建）\n" +
            "之后不要再点击 Build & Publish（构建并发布）。",
            "ワールドを読み込みました！\n\n" +
            "元の .vrcw ファイルは変更されていません。\n\n" +
            "{0}{1}{2}{3}{4}" +
            "World ID：{5}\n\n" +
            "次は：VRCW Hotswap > Upload Hotswapped Build（ホットスワップ後のビルドをアップロード）\n" +
            "このあと Build & Publish（ビルドと公開）は押さないでください。",
            "월드를 불러왔습니다!\n\n" +
            "원본 .vrcw 파일은 변경되지 않았습니다.\n\n" +
            "{0}{1}{2}{3}{4}" +
            "World ID: {5}\n\n" +
            "다음 단계: VRCW Hotswap > Upload Hotswapped Build(핫스왑된 빌드 업로드)\n" +
            "이후에는 Build & Publish(빌드 및 게시)를 누르지 마세요.",
            "¡Mundo cargado!\n\n" +
            "Tu archivo .vrcw original no se ha modificado.\n\n" +
            "{0}{1}{2}{3}{4}" +
            "World ID: {5}\n\n" +
            "Siguiente paso: VRCW Hotswap > Upload Hotswapped Build (subir la build con hotswap)\n" +
            "Después de esto no hagas clic en Build & Publish (compilar y publicar).");
        Add("dialog.lzma_join_note",
            "Packed with LZMA: join may refuse for a while after upload; retrying later usually works.\n\n",
            "这次用的是 LZMA：上传后一段时间内可能进不去世界，过一会儿再试通常就好了。\n\n",
            "今回は LZMA でパックしました：アップロード後しばらくワールドに入れないことがありますが、あとで入り直せばたいてい入れます。\n\n",
            "이번에는 LZMA로 패킹했습니다: 업로드 후 한동안 월드에 들어가지 못할 수 있지만, 나중에 다시 시도하면 대개 들어갈 수 있습니다.\n\n",
            "Empaquetado con LZMA: después de subirlo puede que no te deje entrar durante un rato, pero reintentarlo más tarde suele funcionar.\n\n");
        Add("dialog.nothing_to_reset",
            "Nothing to reset.\nNo hotswap is loaded right now.",
            "没什么可重置的。\n当前没有加载任何热替换。",
            "リセットするものがありません。\n現在ホットスワップは読み込まれていません。",
            "초기화할 것이 없습니다.\n지금은 불러온 핫스왑이 없습니다.",
            "No hay nada que reiniciar.\nAhora mismo no hay ningún hotswap cargado.");
        Add("dialog.reset_confirm_upload_running",
            "An upload is still running.\n\nCancel the upload and clear the current hotswap?\n\nThis only resets this tool. It does not delete worlds on VRChat.",
            "还有一个上传在进行中。\n\n要取消上传并清除当前热替换吗？\n\n这只会重置这个工具，不会删掉你在 VRChat 上的世界。",
            "アップロードがまだ実行中です。\n\nアップロードをキャンセルして、現在のホットスワップをクリアしますか？\n\nこれはこのツールをリセットするだけで、VRChat 上のワールドは削除されません。",
            "업로드가 아직 실행 중입니다.\n\n업로드를 취소하고 현재 핫스왑을 지울까요?\n\n이 도구만 초기화하며, VRChat에 있는 월드는 삭제되지 않습니다.",
            "Todavía hay una subida en marcha.\n\n¿Cancelar la subida y borrar el hotswap actual?\n\nEsto solo reinicia la herramienta. No borra mundos de VRChat.");
        Add("dialog.reset_confirm_busy",
            "Something is still running (load/inspect/pack).\n\nCancel it and clear the current hotswap?\n\nThis only resets this tool so you can load another file.",
            "还有操作在进行中（加载 / 检查 / 打包）。\n\n要取消它并清除当前热替换吗？\n\n这只会重置这个工具，好让你加载别的文件。",
            "まだ処理が実行中です（読み込み / 確認 / パック）。\n\nキャンセルして現在のホットスワップをクリアしますか？\n\nこれはこのツールをリセットするだけで、別のファイルを読み込めるようになります。",
            "아직 작업이 실행 중입니다(불러오기 / 확인 / 패킹).\n\n취소하고 현재 핫스왑을 지울까요?\n\n이 도구만 초기화하므로 다른 파일을 불러올 수 있습니다.",
            "Todavía hay algo en marcha (carga / inspección / empaquetado).\n\n¿Cancelarlo y borrar el hotswap actual?\n\nEsto solo reinicia la herramienta para que puedas cargar otro archivo.");
        Add("dialog.reset_confirm_idle",
            "Clear the current hotswap?\n\nThis only resets this tool so you can load another file.\nNothing is uploaded.",
            "要清除当前热替换吗？\n\n这只会重置这个工具，好让你加载别的文件。\n不会上传任何东西。",
            "現在のホットスワップをクリアしますか？\n\nこれはこのツールをリセットするだけで、別のファイルを読み込めるようになります。\n何もアップロードされません。",
            "현재 핫스왑을 지울까요?\n\n이 도구만 초기화하므로 다른 파일을 불러올 수 있습니다.\n아무것도 업로드되지 않습니다.",
            "¿Borrar el hotswap actual?\n\nEsto solo reinicia la herramienta para que puedas cargar otro archivo.\nNo se sube nada.");
        Add("dialog.reset_done_upload",
            "Upload cancel requested and hotswap cleared.\n\nYou can Load Hotswap File again.",
            "已请求取消上传，热替换也清掉了。\n\n现在可以重新用 Load Hotswap File（加载热替换文件）。",
            "アップロードのキャンセルを要求し、ホットスワップをクリアしました。\n\nまた Load Hotswap File（ホットスワップファイルを読み込み）から始められます。",
            "업로드 취소를 요청하고 핫스왑을 지웠습니다.\n\n다시 Load Hotswap File(핫스왑 파일 불러오기)부터 시작할 수 있습니다.",
            "Se pidió cancelar la subida y se borró el hotswap.\n\nYa puedes volver a usar Load Hotswap File (cargar archivo de hotswap).");
        Add("dialog.reset_done",
            "Cleared.\n\nYou can Load Hotswap File again.",
            "已清除。\n\n现在可以重新用 Load Hotswap File（加载热替换文件）。",
            "クリアしました。\n\nまた Load Hotswap File（ホットスワップファイルを読み込み）から始められます。",
            "지웠습니다.\n\n다시 Load Hotswap File(핫스왑 파일 불러오기)부터 시작할 수 있습니다.",
            "Borrado.\n\nYa puedes volver a usar Load Hotswap File (cargar archivo de hotswap).");
        Add("dialog.reset_failed",
            "Reset failed:\n{0}",
            "重置失败：\n{0}",
            "リセットに失敗しました：\n{0}",
            "초기화에 실패했습니다:\n{0}",
            "El reinicio falló:\n{0}");
        Add("dialog.hotswapped_missing",
            "The hotswapped file is missing.\n\nLoad Hotswap File again.",
            "热替换后的文件不见了。\n\n请重新用一次 Load Hotswap File（加载热替换文件）。",
            "ホットスワップ後のファイルが見つかりません。\n\nもう一度 Load Hotswap File（ホットスワップファイルを読み込み）を実行してください。",
            "핫스왑된 파일이 없습니다.\n\nLoad Hotswap File(핫스왑 파일 불러오기)을 다시 실행하세요.",
            "Falta el archivo con hotswap.\n\nVuelve a usar Load Hotswap File (cargar archivo de hotswap).");
        Add("dialog.hotswapped_changed",
            "The hotswapped file changed.\n\nBuild & Publish probably overwrote it.\n\nLoad Hotswap File again.",
            "热替换后的文件被改动过了。\n\n很可能是 Build & Publish（构建并发布）把它覆盖掉了。\n\n请重新用一次 Load Hotswap File（加载热替换文件）。",
            "ホットスワップ後のファイルが変更されています。\n\nおそらく Build & Publish（ビルドと公開）で上書きされました。\n\nもう一度 Load Hotswap File（ホットスワップファイルを読み込み）を実行してください。",
            "핫스왑된 파일이 변경되었습니다.\n\n아마 Build & Publish(빌드 및 게시)가 덮어썼습니다.\n\nLoad Hotswap File(핫스왑 파일 불러오기)을 다시 실행하세요.",
            "El archivo con hotswap ha cambiado.\n\nSeguramente lo sobrescribió Build & Publish (compilar y publicar).\n\nVuelve a usar Load Hotswap File (cargar archivo de hotswap).");
        Add("dialog.already_busy",
            "Already busy.\n\nWait for it to finish, or use Reset Current Hotswap, then try {0} again.",
            "现在正忙。\n\n等它跑完，或者用一下 Reset Current Hotswap（重置当前热替换），然后再试一次{0}。",
            "ほかの処理を実行中です。\n\n終わるまで待つか、Reset Current Hotswap（現在のホットスワップをリセット）を使ってから、もう一度{0}を実行してください。",
            "다른 작업이 실행 중입니다.\n\n끝날 때까지 기다리거나 Reset Current Hotswap(현재 핫스왑 초기화)을 사용한 뒤 다시 시도하세요.\n\n요청한 작업: {0}",
            "Ya hay algo en marcha.\n\nEspera a que termine, o usa Reset Current Hotswap (reiniciar el hotswap actual), y vuelve a intentarlo.\n\nAcción pedida: {0}");
        Add("dialog.exit_play_mode",
            "Exit Play Mode first.",
            "请先退出 Play Mode（播放模式）。",
            "先に Play Mode（再生モード）を終了してください。",
            "먼저 Play Mode(재생 모드)를 종료하세요.",
            "Sal primero del Play Mode (modo de reproducción).");
        Add("dialog.preparing_cancelled",
            "Cancelled while preparing the world file.",
            "已在准备世界文件的阶段取消。",
            "ワールドファイルの準備中にキャンセルしました。",
            "월드 파일을 준비하는 중에 취소했습니다.",
            "Cancelado mientras se preparaba el archivo de mundo.");
        Add("dialog.open_vrcw_failed",
            "Could not open that .vrcw file.\n\n{0}\n\nCheck the Console, then try again.",
            "打不开这个 .vrcw 文件。\n\n{0}\n\n看一下 Console（控制台），然后再试一次。",
            "この .vrcw ファイルを開けませんでした。\n\n{0}\n\nConsole（コンソール）を確認して、もう一度試してください。",
            "이 .vrcw 파일을 열 수 없습니다.\n\n{0}\n\nConsole(콘솔)을 확인한 뒤 다시 시도하세요.",
            "No se pudo abrir ese archivo .vrcw.\n\n{0}\n\nMira la Console (consola) y vuelve a intentarlo.");

        Add("android.disclaimer",
            "Android hotswap is barely tested (Quest, Pico, phones, etc.).\nIt may not work. Android worlds must be under 100 MB after packing.",
            "Android 热替换几乎没怎么测试过（Quest、Pico、手机等）。\n它有可能根本用不了。Android 世界打包后必须小于 100 MB。",
            "Android のホットスワップはほとんどテストできていません（Quest、Pico、スマホなど）。\n動かないこともあります。Android のワールドはパック後 100 MB 未満である必要があります。",
            "Android 핫스왑은 테스트가 거의 되지 않았습니다(Quest, Pico, 휴대폰 등).\n동작하지 않을 수 있습니다. Android 월드는 패킹 후 100 MB 미만이어야 합니다.",
            "El hotswap en Android está casi sin probar (Quest, Pico, móviles, etc.).\nPuede que no funcione. Los mundos de Android tienen que quedar por debajo de 100 MB tras empaquetar.");
        Add("android.packed_ok",
            "Android packed size: {0} (under {1} limit). OK to upload.\n\n",
            "Android 打包后大小：{0}（在 {1} 限制以内），可以上传。\n\n",
            "Android のパック後サイズ：{0}（{1} の上限内）。アップロードできます。\n\n",
            "Android 패킹 후 용량: {0}({1} 상한 이내). 업로드할 수 있습니다.\n\n",
            "Tamaño empaquetado en Android: {0} (por debajo del límite de {1}). Se puede subir.\n\n");
        Add("android.packed_over",
            "Android packed size: {0} (over {1} limit). Upload will probably fail.\n\n",
            "Android 打包后大小：{0}（超过 {1} 限制），上传大概率会失败。\n\n",
            "Android のパック後サイズ：{0}（{1} の上限超え）。アップロードはおそらく失敗します。\n\n",
            "Android 패킹 후 용량: {0}({1} 상한 초과). 업로드는 실패할 가능성이 높습니다.\n\n",
            "Tamaño empaquetado en Android: {0} (por encima del límite de {1}). La subida probablemente falle.\n\n");

        Add("oversize.upload_message",
            "This file looks too big for {0}.\n\nSize: {1}\nUsual limit: about {2}\n\n{3}",
            "这个文件对 {0} 来说好像太大了。\n\n大小：{1}\n一般限制：约 {2}\n\n{3}",
            "このファイルは {0} には大きすぎるようです。\n\nサイズ：{1}\n一般的な上限：約 {2}\n\n{3}",
            "이 파일은 {0}에 너무 큰 것 같습니다.\n\n용량: {1}\n일반적인 상한: 약 {2}\n\n{3}",
            "Este archivo parece demasiado grande para {0}.\n\nTamaño: {1}\nLímite habitual: unos {2}\n\n{3}");
        Add("oversize.android_over",
            "Android worlds must be under 100 MB. This is over that.",
            "Android 世界必须小于 100 MB，这个文件已经超了。",
            "Android のワールドは 100 MB 未満である必要がありますが、このファイルは超えています。",
            "Android 월드는 100 MB 미만이어야 하지만, 이 파일은 초과했습니다.",
            "Los mundos de Android tienen que estar por debajo de 100 MB, y este se pasa.");
        Add("oversize.android_packed",
            "Android worlds must stay under 100 MB packed.",
            "Android 世界打包后也得保持在 100 MB 以内。",
            "Android のワールドはパック後も 100 MB 未満に収める必要があります。",
            "Android 월드는 패킹 후에도 100 MB 미만이어야 합니다.",
            "Los mundos de Android tienen que quedar por debajo de 100 MB una vez empaquetados.");
        Add("oversize.pc_hopeless",
            "Over about 2.5 GB almost never works.",
            "超过约 2.5 GB 基本不可能成功。",
            "約 2.5 GB を超えるとほぼ成功しません。",
            "약 2.5 GB를 넘으면 거의 성공하지 않습니다.",
            "Por encima de unos 2.5 GB casi nunca funciona.");
        Add("oversize.pc_unlikely",
            "This size often fails with \"That file is much too big\".",
            "这个体积经常会失败，提示 “That file is much too big”。",
            "このサイズは「That file is much too big」と表示されて失敗しがちです。",
            "이 용량은 'That file is much too big' 메시지와 함께 실패하는 경우가 많습니다.",
            "Con este tamaño suele fallar con el mensaje 'That file is much too big'.");
        Add("oversize.pc_maybe",
            "It might still work. If not, VRChat will say the file is too big.",
            "也许还是能过；如果不行，VRChat 会提示文件太大。",
            "うまくいくこともあります。だめな場合は VRChat がファイルが大きすぎると表示します。",
            "그래도 될 수 있습니다. 안 되면 VRChat이 파일이 너무 크다고 알려줍니다.",
            "Aún puede funcionar. Si no, VRChat dirá que el archivo es demasiado grande.");

        Add("pack.reason.compressor_missing",
            "Compressor missing; Unity LZ4Runtime only (AssetsTools unavailable).",
            "找不到压缩器，只能用 Unity 的 LZ4Runtime（AssetsTools 用不了）。",
            "コンプレッサーがないため Unity の LZ4Runtime のみです（AssetsTools は利用できません）。",
            "압축기가 없어 Unity의 LZ4Runtime만 사용할 수 있습니다(AssetsTools 사용 불가).",
            "Falta el compresor; solo LZ4Runtime de Unity (AssetsTools no disponible).");
        Add("pack.reason.size_unknown",
            "Size unknown; LZ4 is the usual world default (best join odds).",
            "体积未知。LZ4 是世界最常用的选择，进入成功率也最高。",
            "サイズ不明。LZ4 がワールドでは一般的で、参加できる可能性も高めです。",
            "용량 불명. LZ4가 월드에서 가장 일반적이고 입장 성공률도 높습니다.",
            "Tamaño desconocido. LZ4 es lo habitual en mundos y lo que más facilita entrar.");
        Add("pack.reason.lz4_under_limit",
            "Estimated LZ4 under the {0} limit ({1}); best join odds.",
            "预计 LZ4 打包后不超过 {0} 的限制（{1}），进入成功率最高。",
            "推定 LZ4 は {0} の上限（{1}）に収まります。参加できる可能性が最も高い選択です。",
            "예상 LZ4가 {0} 상한({1}) 이내입니다. 입장 성공률이 가장 높은 선택입니다.",
            "El LZ4 estimado cabe en el límite de {0} ({1}). Es la opción con más probabilidades de entrar.");
        Add("pack.reason.android_lzma_fit",
            "Estimated LZ4 over Android 100 MB; LZMA is more likely to fit.",
            "预计 LZ4 会超过 Android 的 100 MB，用 LZMA 更有机会压进去。",
            "推定 LZ4 は Android の 100 MB を超えます。LZMA なら収まる可能性が高いです。",
            "예상 LZ4가 Android 100 MB를 초과합니다. LZMA면 들어갈 가능성이 높습니다.",
            "El LZ4 estimado pasa de los 100 MB de Android. Con LZMA es más probable que quepa.");
        Add("pack.reason.android_lzma_maybe",
            "Estimated LZ4 over Android 100 MB; try LZMA (may still be over).",
            "预计 LZ4 会超过 Android 的 100 MB，可以试试 LZMA，但也可能压不下来。",
            "推定 LZ4 は Android の 100 MB を超えます。LZMA を試せますが、それでも超えるかもしれません。",
            "예상 LZ4가 Android 100 MB를 초과합니다. LZMA를 시도할 수 있지만 그래도 초과할 수 있습니다.",
            "El LZ4 estimado pasa de los 100 MB de Android. Puedes probar LZMA, aunque quizá siga pasándose.");
        Add("pack.reason.lz4_soft_zone",
            "Estimated LZ4 is in the soft zone ({0}-{1}); still prefer LZ4 for join odds.",
            "预计 LZ4 落在临界区间（{0}-{1}）。为了进入成功率，还是优先用 LZ4。",
            "推定 LZ4 は境界の範囲（{0}-{1}）です。参加のしやすさを優先して LZ4 をおすすめします。",
            "예상 LZ4가 경계 구간({0}-{1})입니다. 입장 성공률을 위해 그래도 LZ4를 권장합니다.",
            "El LZ4 estimado está en la zona límite ({0}-{1}). Aun así, mejor LZ4 para poder entrar.");
        Add("pack.reason.lzma_hopeless",
            "Estimated LZ4 over soft limit; LZMA recommended for size, but it may still be too large.",
            "预计 LZ4 会超过软上限。为了体积建议用 LZMA，不过可能还是太大。",
            "推定 LZ4 がソフト上限を超えます。サイズ面では LZMA をおすすめしますが、それでも大きすぎるかもしれません。",
            "예상 LZ4가 소프트 상한을 넘습니다. 용량 때문에 LZMA를 권장하지만 그래도 너무 클 수 있습니다.",
            "El LZ4 estimado pasa del límite recomendado. Por tamaño conviene LZMA, aunque quizá siga siendo demasiado grande.");
        Add("pack.reason.lzma_soft",
            "Estimated LZ4 over soft limit; LZMA recommended (size escape hatch). Join may lag at first.",
            "预计 LZ4 会超过软上限，建议用 LZMA 来救体积。刚上传后可能一时进不去。",
            "推定 LZ4 がソフト上限を超えます。サイズ対策として LZMA をおすすめします。最初はワールドに入れないことがあります。",
            "예상 LZ4가 소프트 상한을 넘습니다. 용량 대책으로 LZMA를 권장합니다. 처음에는 월드에 들어가지 못할 수 있습니다.",
            "El LZ4 estimado pasa del límite recomendado. LZMA es la salida por tamaño. Al principio puede costar entrar.");
        Add("pack.reason.lzma_under_limit",
            "Estimated LZ4 over soft limit; LZMA recommended to get under upload size. Join may lag at first.",
            "预计 LZ4 会超过软上限，建议用 LZMA 压到上传限制以内。刚上传后可能一时进不去。",
            "推定 LZ4 がソフト上限を超えます。アップロード上限に収めるため LZMA をおすすめします。最初はワールドに入れないことがあります。",
            "예상 LZ4가 소프트 상한을 넘습니다. 업로드 상한에 맞추려면 LZMA를 권장합니다. 처음에는 월드에 들어가지 못할 수 있습니다.",
            "El LZ4 estimado pasa del límite recomendado. LZMA lo deja por debajo del límite de subida. Al principio puede costar entrar.");

        Add("pack.mode", "Mode", "模式", "モード", "모드", "Modo");
        Add("pack.mode.simple", "Simple", "简单", "シンプル", "간단", "Simple");
        Add("pack.mode.advanced", "Advanced", "高级", "詳細", "고급", "Avanzado");
        Add("pack.mode.simple_desc",
            "Simple: recommended pack path for upload.",
            "简单模式：只显示推荐用于上传的打包方式。",
            "シンプル：アップロード向けのおすすめ設定だけを表示します。",
            "간단: 업로드용 권장 패킹 방식만 표시합니다.",
            "Simple: solo la forma de empaquetar recomendada para subir.");
        Add("pack.mode.advanced_desc",
            "Advanced: includes testing options (Uncompressed / LZ4Runtime).",
            "高级模式：额外显示测试用的选项（Uncompressed / LZ4Runtime）。",
            "詳細：テスト用の選択肢（Uncompressed / LZ4Runtime）も表示します。",
            "고급: 테스트용 옵션(Uncompressed / LZ4Runtime)도 표시합니다.",
            "Avanzado: incluye las opciones de prueba (Uncompressed / LZ4Runtime).");
        Add("pack.recommended_path", "Recommended path", "推荐做法", "おすすめの方法", "권장 방식", "Opción recomendada");
        Add("pack.other_options", "Other pack options", "其他打包方式", "ほかのパック方法", "다른 패킹 방식", "Otras formas de empaquetar");
        Add("pack.testing_options", "Testing options", "测试用选项", "テスト用の選択肢", "테스트용 옵션", "Opciones de prueba");
        Add("pack.testing_desc",
            "Not for normal uploads. Weaker or larger than AssetsTools LZ4/LZMA.",
            "不适合正常上传：压缩更弱，或者文件比 AssetsTools 的 LZ4/LZMA 更大。",
            "通常のアップロードには向きません。AssetsTools の LZ4/LZMA より圧縮が弱いか、ファイルが大きくなります。",
            "일반 업로드에는 적합하지 않습니다. AssetsTools의 LZ4/LZMA보다 압축이 약하거나 파일이 커집니다.",
            "No sirven para subidas normales. Comprimen menos, o dan archivos más grandes que LZ4/LZMA de AssetsTools.");
        Add("pack.badge.recommended", "recommended", "推荐", "おすすめ", "권장", "recomendado");
        Add("pack.badge.matches_source", "matches source", "与原文件一致", "元ファイルと同じ", "원본과 동일", "igual al original");
        Add("pack.helpbox",
            "Platform: {0}  |  Soft limit ~{1}{2}\n" +
            "Source: {3}{4}\n" +
            "Uncompressed now: {5}\n" +
            "Est. LZ4: {6}  |  Est. LZMA: {7}\n\n" +
            "Recommended: {8}\n" +
            "{9}",
            "平台：{0}  |  软上限约 {1}{2}\n" +
            "源文件：{3}{4}\n" +
            "解压后大小：{5}\n" +
            "预计 LZ4：{6}  |  预计 LZMA：{7}\n\n" +
            "推荐：{8}\n" +
            "{9}",
            "プラットフォーム：{0}  |  ソフト上限 約{1}{2}\n" +
            "元ファイル：{3}{4}\n" +
            "展開後のサイズ：{5}\n" +
            "推定 LZ4：{6}  |  推定 LZMA：{7}\n\n" +
            "おすすめ：{8}\n" +
            "{9}",
            "플랫폼: {0}  |  소프트 상한 약 {1}{2}\n" +
            "원본: {3}{4}\n" +
            "압축 해제 용량: {5}\n" +
            "예상 LZ4: {6}  |  예상 LZMA: {7}\n\n" +
            "권장: {8}\n" +
            "{9}",
            "Plataforma: {0}  |  Límite recomendado ~{1}{2}\n" +
            "Original: {3}{4}\n" +
            "Sin comprimir ahora: {5}\n" +
            "LZ4 estimado: {6}  |  LZMA estimado: {7}\n\n" +
            "Recomendado: {8}\n" +
            "{9}");
        Add("pack.helpbox.unlikely_suffix",
            " / unlikely ~{0}",
            " / 高风险线约 {0}",
            " / 高リスク 約{0}",
            " / 고위험 약 {0}",
            " / poco probable ~{0}");
        Add("pack.compressor_missing_warning",
            "Compressor exe missing. Packing will use Unity LZ4Runtime only.",
            "找不到 compressor exe，打包只能用 Unity 的 LZ4Runtime。",
            "compressor exe が見つかりません。パックは Unity の LZ4Runtime のみになります。",
            "compressor exe를 찾을 수 없습니다. 패킹은 Unity의 LZ4Runtime만 사용합니다.",
            "Falta compressor exe. Se empaquetará solo con LZ4Runtime de Unity.");
        Add("pack.choice.uncompressed.title",
            "Uncompressed  (no wait)",
            "Uncompressed  （不用等）",
            "Uncompressed  （待ち時間なし）",
            "Uncompressed  (대기 없음)",
            "Uncompressed  (sin espera)");
        Add("pack.choice.uncompressed.subtitle",
            "Leave unpacked after ID rewrite. Largest file. Testing only.",
            "改完 ID 后不压缩。文件最大，只适合测试。",
            "ID を書き換えたあと圧縮しません。ファイルは最大。テスト用です。",
            "ID를 바꾼 뒤 압축하지 않습니다. 파일이 가장 큽니다. 테스트용입니다.",
            "Deja el archivo sin comprimir tras cambiar el ID. El más grande. Solo para pruebas.");
        Add("pack.choice.lz4runtime.title",
            "LZ4Runtime  (no wait)",
            "LZ4Runtime  （不用等）",
            "LZ4Runtime  （待ち時間なし）",
            "LZ4Runtime  (대기 없음)",
            "LZ4Runtime  (sin espera)");
        Add("pack.choice.lz4runtime.subtitle",
            "Unity pack. Weaker than AssetsTools LZ4. Testing / fallback.",
            "Unity 自带打包，压缩率不如 AssetsTools 的 LZ4。用于测试或回退。",
            "Unity 標準のパック。AssetsTools の LZ4 より弱めです。テストや代替用。",
            "Unity 기본 패킹. AssetsTools의 LZ4보다 약합니다. 테스트 또는 대체용.",
            "Empaquetado de Unity. Comprime menos que LZ4 de AssetsTools. Pruebas o respaldo.");
        Add("pack.choice.lz4.title",
            "LZ4  (some wait)",
            "LZ4  （要等一会）",
            "LZ4  （少し待ちます）",
            "LZ4  (조금 대기)",
            "LZ4  (espera corta)");
        Add("pack.choice.lz4.subtitle",
            "AssetsTools. Usual for worlds. Best join odds when size allows.",
            "AssetsTools。世界最常用的选择，体积允许时进入成功率最高。",
            "AssetsTools。ワールドでは一般的。サイズが許せば参加できる可能性が最も高いです。",
            "AssetsTools. 월드에서 일반적입니다. 용량이 허용되면 입장 성공률이 가장 높습니다.",
            "AssetsTools. Lo habitual en mundos. Si el tamaño lo permite, es lo mejor para entrar.");
        Add("pack.choice.lzma.title",
            "LZMA  (long wait)",
            "LZMA  （要等很久）",
            "LZMA  （かなり待ちます）",
            "LZMA  (오래 대기)",
            "LZMA  (espera larga)");
        Add("pack.choice.lzma.subtitle",
            "AssetsTools. Smallest. Join may fail at first; retrying later usually works.",
            "AssetsTools。体积最小。刚开始可能进不去，过一会儿再试通常就好了。",
            "AssetsTools。最小サイズ。最初は入れないことがありますが、あとで入り直せばたいてい入れます。",
            "AssetsTools. 용량이 가장 작습니다. 처음에는 못 들어갈 수 있지만 나중에 다시 시도하면 대개 들어갑니다.",
            "AssetsTools. El más pequeño. Al principio puede fallar al entrar; reintentar más tarde suele funcionar.");

        Add("idpicker.title",
            "Pick the main world ID",
            "选择主 world ID",
            "メインの world ID を選択",
            "메인 world ID 선택",
            "Elige el world ID principal");
        Add("idpicker.help",
            "Pick this world's own ID.\nDon't pick portal links unless you mean to.",
            "请选这个世界自己的 ID。\n注意不要误选传送门链接。",
            "このワールド自身の ID を選んでください。\nポータルのリンクを誤って選ばないよう注意してください。",
            "이 월드 자체의 ID를 선택하세요.\n포털 링크를 잘못 선택하지 않도록 주의하세요.",
            "Elige el ID de este mundo.\nNo elijas enlaces de portales sin querer.");

        Add("about.version", "Version {0}", "版本 {0}", "バージョン {0}", "버전 {0}", "Versión {0}");
        Add("about.tested_working", "Tested & working:", "已测试可用：", "動作確認済み：", "동작 확인:", "Probado y funcionando:");
        Add("about.tested_working_2019",
            "• Worlds SDK {0} / Unity {1}",
            "• Worlds SDK {0} / Unity {1}",
            "• Worlds SDK {0} / Unity {1}",
            "• Worlds SDK {0} / Unity {1}",
            "• Worlds SDK {0} / Unity {1}");
        Add("about.tested_working_2022_6f1",
            "• Worlds SDK {0} / Unity {1} with PC worlds that match 6f1",
            "• Worlds SDK {0} / Unity {1}，配 6f1 构建的 PC 世界",
            "• Worlds SDK {0} / Unity {1}、6f1 でビルドされた PC ワールド",
            "• Worlds SDK {0} / Unity {1}, 6f1로 빌드한 PC 월드",
            "• Worlds SDK {0} / Unity {1}, con mundos de PC hechos en 6f1");
        Add("about.tested_working_2022_22f1",
            "• Worlds SDK {0} / Unity {1} with PC worlds that match 22f1",
            "• Worlds SDK {0} / Unity {1}，配 22f1 构建的 PC 世界",
            "• Worlds SDK {0} / Unity {1}、22f1 でビルドされた PC ワールド",
            "• Worlds SDK {0} / Unity {1}, 22f1로 빌드한 PC 월드",
            "• Worlds SDK {0} / Unity {1}, con mundos de PC hechos en 22f1");
        Add("about.partially_tested",
            "Partially tested & sometimes working:",
            "测试不完整，有时能用：",
            "一部のみ確認、動く場合もあります：",
            "일부만 확인, 동작할 때도 있음:",
            "Probado en parte, funciona a veces:");
        Add("about.partially_tested_dwr",
            "• Worlds SDK {0} / Unity {1} with 22f2-DWR world bundles",
            "• Worlds SDK {0} / Unity {1}，配 22f2-DWR 的世界 bundle",
            "• Worlds SDK {0} / Unity {1}、22f2-DWR のワールドバンドル",
            "• Worlds SDK {0} / Unity {1}, 22f2-DWR 월드 번들",
            "• Worlds SDK {0} / Unity {1}, con bundles de mundo 22f2-DWR");
        Add("about.description",
            "Rewrites a .vrcw to your world ID, swaps it onto the SDK's last build, and lets you upload it.\n" +
            "Works best when the file's Unity version matches your Editor.\n" +
            "Only use this on your own worlds.",
            "把 .vrcw 里的 world ID 改成你自己的，替换掉 SDK 最近一次构建的文件，然后上传。\n" +
            "文件的 Unity 版本和你的编辑器一致时效果最好。\n" +
            "请只对你自己的世界使用。",
            ".vrcw の world ID を自分のものに書き換え、SDK の直近のビルドと差し替えてアップロードします。\n" +
            "ファイルの Unity バージョンがエディターと一致しているときに最も安定します。\n" +
            "自分のワールドにのみ使用してください。",
            ".vrcw의 world ID를 내 것으로 바꾸고, SDK의 마지막 빌드와 교체해 업로드합니다.\n" +
            "파일의 Unity 버전이 에디터와 같을 때 가장 안정적입니다.\n" +
            "본인 월드에만 사용하세요.",
            "Cambia el world ID de un .vrcw por el tuyo, lo pone en la última build del SDK y te deja subirlo.\n" +
            "Funciona mejor si la versión de Unity del archivo coincide con tu Editor.\n" +
            "Úsalo solo con tus propios mundos.");
        Add("about.credits", "Credits", "致谢", "クレジット", "크레딧", "Créditos");
        Add("about.maintained_by", "Maintained by: ", "维护者：", "メンテナー：", "관리자: ", "Mantenido por: ");
        Add("about.based_on_prefix", "Based on ", "基于 ", "元になったのは ", "기반: ", "Basado en el Hotswap Script de ");
        Add("about.based_on_suffix", "'s Hotswap Script", " 的 Hotswap Script", " の Hotswap Script", "의 Hotswap Script", ".");
        Add("about.needs_sdk",
            "Needs the VRChat Worlds SDK.",
            "需要 VRChat Worlds SDK。",
            "VRChat Worlds SDK が必要です。",
            "VRChat Worlds SDK가 필요합니다.",
            "Necesita el VRChat Worlds SDK.");

        Add("progress.preparing_world",
            "Preparing your world file...",
            "正在准备世界文件...",
            "ワールドファイルを準備中...",
            "월드 파일 준비 중...",
            "Preparando tu archivo de mundo...");
        Add("progress.reading", "Reading...", "读取中...", "読み込み中...", "읽는 중...", "Leyendo...");
        Add("progress.scanning_world",
            "Scanning world file...",
            "正在扫描世界文件...",
            "ワールドファイルをスキャン中...",
            "월드 파일 스캔 중...",
            "Analizando el archivo de mundo...");
        Add("progress.updating_ids", "Updating IDs...", "正在更新 ID...", "ID を更新中...", "ID 업데이트 중...", "Actualizando los ID...");
        Add("progress.starting_upload", "Starting upload...", "正在开始上传...", "アップロードを開始中...", "업로드 시작 중...", "Empezando la subida...");
        Add("progress.uploading", "Uploading...", "上传中...", "アップロード中...", "업로드 중...", "Subiendo...");
        Add("progress.packing", "Packing ({0})...", "打包中（{0}）...", "パック中（{0}）...", "패킹 중({0})...", "Empaquetando ({0})...");
        Add("progress.packing_unity_lz4runtime",
            "Packing (Unity LZ4Runtime)...",
            "打包中（Unity LZ4Runtime）...",
            "パック中（Unity LZ4Runtime）...",
            "패킹 중(Unity LZ4Runtime)...",
            "Empaquetando (LZ4Runtime de Unity)...");
        Add("progress.packing_uncompressed",
            "Packing (Uncompressed)...",
            "打包中（Uncompressed）...",
            "パック中（Uncompressed）...",
            "패킹 중(Uncompressed)...",
            "Empaquetando (Uncompressed)...");
        Add("progress.checking_result",
            "Checking result...",
            "正在检查结果...",
            "結果を確認中...",
            "결과 확인 중...",
            "Comprobando el resultado...");

        Add("inspect.truncated_suffix",
            "\n\n...(see Console for full list)",
            "\n\n……（完整列表请看 Console 控制台）",
            "\n\n...（全リストは Console コンソールで確認）",
            "\n\n...(전체 목록은 Console 콘솔에서 확인)",
            "\n\n...(la lista completa está en la Console)");
        Add("inspect.file", "File: {0}", "文件：{0}", "ファイル：{0}", "파일: {0}", "Archivo: {0}");
        Add("inspect.size", "Size: {0}", "大小：{0}", "サイズ：{0}", "용량: {0}", "Tamaño: {0}");
        Add("inspect.built_with_unity",
            "Built with Unity: {0}",
            "构建版本：Unity {0}",
            "ビルド元：Unity {0}",
            "빌드 버전: Unity {0}",
            "Compilado con Unity: {0}");
        Add("inspect.bundle_format", "Bundle format: {0}", "Bundle 格式：{0}", "バンドル形式：{0}", "번들 형식: {0}", "Formato del bundle: {0}");
        Add("inspect.dwr_yes",
            "DWR: yes (VRChat/custom build; hotswap may work, join not guaranteed)",
            "DWR：是（VRChat / 自定义构建，热替换可能能用，但不保证进得去）",
            "DWR：はい（VRChat / 独自ビルド。ホットスワップは動く場合がありますが、参加は保証されません）",
            "DWR: 예(VRChat / 커스텀 빌드. 핫스왑은 될 수 있지만 입장은 보장되지 않습니다)",
            "DWR: sí (build de VRChat o personalizada; el hotswap puede funcionar, entrar no está garantizado)");
        Add("inspect.version_check_ok",
            "Version check: OK ({0})",
            "版本检查：通过（{0}）",
            "バージョン確認：OK（{0}）",
            "버전 확인: OK({0})",
            "Comprobación de versión: OK ({0})");
        Add("inspect.version_check_dwr",
            "Version check: DWR (want {0}, or match your Editor {1}; often works, join can still fail)",
            "版本检查：DWR（建议用 {0}，或与当前编辑器 {1} 一致；一般能用，但仍可能进不去）",
            "バージョン確認：DWR（{0} を推奨、またはエディター {1} と一致。多くは動きますが参加に失敗することもあります）",
            "버전 확인: DWR({0} 권장, 또는 현재 에디터 {1}와 일치. 대개 동작하지만 입장에 실패할 수 있습니다)",
            "Comprobación de versión: DWR (mejor {0}, o que coincida con tu Editor {1}; suele funcionar, pero entrar puede fallar)");
        Add("inspect.version_check_wrong",
            "Version check: WRONG (want {0}, or match your Editor {1})",
            "版本检查：不匹配（建议用 {0}，或与当前编辑器 {1} 一致）",
            "バージョン確認：不一致（{0} を推奨、またはエディター {1} に合わせてください）",
            "버전 확인: 불일치({0} 권장, 또는 현재 에디터 {1}에 맞추세요)",
            "Comprobación de versión: NO COINCIDE (mejor {0}, o que coincida con tu Editor {1})");
        Add("inspect.built_with_unity_unreadable",
            "Built with Unity: (couldn't read: {0})",
            "构建版本：（读不出来：{0}）",
            "ビルド元：（読み取れません：{0}）",
            "빌드 버전: (읽을 수 없음: {0})",
            "Compilado con Unity: (no se pudo leer: {0})");
        Add("inspect.compression_uncompressed",
            "Compression: uncompressed ({0})",
            "压缩：未压缩（{0}）",
            "圧縮：なし（{0}）",
            "압축: 없음({0})",
            "Compresión: sin comprimir ({0})");
        Add("inspect.compression_value", "Compression: {0} ({1})", "压缩：{0}（{1}）", "圧縮：{0}（{1}）", "압축: {0}({1})", "Compresión: {0} ({1})");
        Add("inspect.compression_unknown",
            "Compression: unknown ({0})",
            "压缩：未知（{0}）",
            "圧縮：不明（{0}）",
            "압축: 알 수 없음({0})",
            "Compresión: desconocida ({0})");
        Add("inspect.platform_guess",
            "Platform guess: {0}",
            "平台推测：{0}",
            "プラットフォーム推定：{0}",
            "플랫폼 추정: {0}",
            "Plataforma estimada: {0}");
        Add("inspect.main_world_id",
            "Main world ID: {0}",
            "主 world ID：{0}",
            "メインの world ID：{0}",
            "메인 world ID: {0}",
            "World ID principal: {0}");
        Add("inspect.main_world_id_missing",
            "Main world ID: (not found)",
            "主 world ID：（未找到）",
            "メインの world ID：（見つかりません）",
            "메인 world ID: (찾을 수 없음)",
            "World ID principal: (no encontrado)");
        Add("inspect.world_ids_found",
            "World IDs found: {0}",
            "找到的 world ID：{0}",
            "見つかった world ID：{0}",
            "찾은 world ID: {0}",
            "World ID encontrados: {0}");
        Add("inspect.scene_names", "Scene names: {0}", "场景名称：{0}", "シーン名：{0}", "씬 이름: {0}", "Nombres de escena: {0}");
        Add("inspect.other_unity_versions",
            "Other Unity versions in file: {0}",
            "文件里出现的其他 Unity 版本：{0}",
            "ファイル内のほかの Unity バージョン：{0}",
            "파일에 있는 다른 Unity 버전: {0}",
            "Otras versiones de Unity en el archivo: {0}");
        Add("inspect.extra_world_ids_hint",
            "Extra world IDs are usually portal links.",
            "多出来的 world ID 一般是传送门链接。",
            "余分な world ID は通常ポータルのリンクです。",
            "남는 world ID는 보통 포털 링크입니다.",
            "Los world ID de sobra suelen ser enlaces de portales.");

        Add("compression.unknown", "unknown", "未知", "不明", "알 수 없음", "desconocida");
        Add("compression.uncompressed", "uncompressed", "未压缩", "未圧縮", "비압축", "sin comprimir");
        Add("compression.mixed", "mixed", "混合", "混在", "혼합", "mixta");
        Add("platform.guess.unknown", "unknown", "未知", "不明", "알 수 없음", "desconocida");
        Add("platform.guess.pc", "PC", "PC", "PC", "PC", "PC");
        Add("platform.guess.android", "Android", "Android", "Android", "Android", "Android");
        Add("platform.guess.ambiguous", "ambiguous", "无法确定", "判別不能", "판별 불가", "ambigua");
        Add("error.unknown", "Unknown error.", "未知错误。", "不明なエラーです。", "알 수 없는 오류입니다.", "Error desconocido.");
        Add("cancel.nothing_uploaded_suffix",
            "\n\nNothing was uploaded.",
            "\n\n没有上传任何东西。",
            "\n\n何もアップロードしていません。",
            "\n\n아무것도 업로드하지 않았습니다.",
            "\n\nNo se subió nada.");
        Add("value.na", "n/a", "不可用", "不明", "알 수 없음", "n/d");
        Add("value.none", "(none)", "（无）", "（なし）", "(없음)", "(ninguno)");
        Add("sdk.version_unknown",
            "unknown (check Packages / Creator Companion)",
            "未知（去 Packages 或 Creator Companion 里看看）",
            "不明（Packages または Creator Companion を確認してください）",
            "알 수 없음(Packages 또는 Creator Companion 확인)",
            "desconocida (revisa Packages / Creator Companion)");

        Add("unity.supported.preferred",
            "matches preferred {0}",
            "与推荐版本 {0} 一致",
            "推奨の {0} と一致",
            "권장 버전 {0}와 일치",
            "coincide con la recomendada {0}");
        Add("unity.supported.editor",
            "matches this Editor ({0})",
            "与当前编辑器一致（{0}）",
            "このエディターと一致（{0}）",
            "현재 에디터와 일치({0})",
            "coincide con este Editor ({0})");
        Add("confirm.unity_unknown",
            "Couldn't read which Unity version made this file.\n\n" +
            "Detail: {0}\n\n" +
            "Best: use a .vrcw that matches this Editor, or a {1} .vrcw on Unity {1}.\n\n" +
            "Continue anyway?",
            "读不出这个文件是用哪个 Unity 版本构建的。\n\n" +
            "详细信息：{0}\n\n" +
            "最好的做法：用与当前编辑器版本一致的 .vrcw，或者在 Unity {1} 里用 {1} 构建的 .vrcw。\n\n" +
            "仍然继续吗？",
            "このファイルをビルドした Unity バージョンを読み取れませんでした。\n\n" +
            "詳細：{0}\n\n" +
            "おすすめ：このエディターと一致する .vrcw を使うか、Unity {1} で {1} の .vrcw を使ってください。\n\n" +
            "それでも続けますか？",
            "이 파일을 빌드한 Unity 버전을 읽을 수 없습니다.\n\n" +
            "자세한 내용: {0}\n\n" +
            "권장: 현재 에디터와 같은 .vrcw를 사용하거나, Unity {1}에서 {1} .vrcw를 사용하세요.\n\n" +
            "그래도 계속할까요?",
            "No se pudo leer con qué versión de Unity se hizo este archivo.\n\n" +
            "Detalle: {0}\n\n" +
            "Lo mejor: usa un .vrcw que coincida con este Editor, o un .vrcw de {1} en Unity {1}.\n\n" +
            "¿Continuar igualmente?");
        Add("confirm.dwr_bundle",
            "This looks like a DWR build:\n  {0}\n\n" +
            "This Editor is:\n  {1}\n\n" +
            "DWR files are VRChat/custom builds, not a normal SDK Build & Publish .vrcw.\n\n" +
            "Hotswap and upload may work, and they are often more likely to work than much older Unity worlds, but joining can still fail.\n\n" +
            "Most reliable: a {2} .vrcw on Unity {2}, or a .vrcw that exactly matches this Editor.\n\n" +
            "Continue anyway?",
            "这个文件看起来是 DWR 构建：\n  {0}\n\n" +
            "当前编辑器是：\n  {1}\n\n" +
            "DWR 文件来自 VRChat 或自定义构建，不是 SDK 用 Build & Publish（构建并发布）正常生成的 .vrcw。\n\n" +
            "热替换和上传有可能成功，成功率通常也比很旧的 Unity 世界高，但还是可能进不去。\n\n" +
            "最稳的做法：在 Unity {2} 里用 {2} 构建的 .vrcw，或者用和当前编辑器完全一致的 .vrcw。\n\n" +
            "仍然继续吗？",
            "これは DWR ビルドのようです：\n  {0}\n\n" +
            "このエディターは：\n  {1}\n\n" +
            "DWR ファイルは VRChat / 独自ビルドで、SDK の Build & Publish（ビルドと公開）で作られた通常の .vrcw ではありません。\n\n" +
            "ホットスワップとアップロードは成功することがあり、かなり古い Unity のワールドより成功しやすい傾向がありますが、参加に失敗する可能性は残ります。\n\n" +
            "最も確実なのは、Unity {2} で {2} の .vrcw を使うか、このエディターと完全に一致する .vrcw を使うことです。\n\n" +
            "それでも続けますか？",
            "DWR 빌드로 보입니다:\n  {0}\n\n" +
            "현재 에디터:\n  {1}\n\n" +
            "DWR 파일은 VRChat / 커스텀 빌드이며, SDK의 Build & Publish(빌드 및 게시)로 만든 일반 .vrcw가 아닙니다.\n\n" +
            "핫스왑과 업로드가 될 수도 있고 훨씬 오래된 Unity 월드보다 성공 가능성이 높은 편이지만, 입장에 실패할 수 있습니다.\n\n" +
            "가장 확실한 방법은 Unity {2}에서 {2} .vrcw를 쓰거나, 현재 에디터와 정확히 같은 .vrcw를 쓰는 것입니다.\n\n" +
            "그래도 계속할까요?",
            "Esto parece una build DWR:\n  {0}\n\n" +
            "Este Editor es:\n  {1}\n\n" +
            "Los archivos DWR son builds de VRChat o personalizadas, no un .vrcw normal de Build & Publish (compilar y publicar) del SDK.\n\n" +
            "El hotswap y la subida pueden funcionar, y suelen tener más posibilidades que los mundos de Unity mucho más antiguos, pero entrar puede fallar.\n\n" +
            "Lo más fiable: un .vrcw de {2} en Unity {2}, o un .vrcw que coincida exactamente con este Editor.\n\n" +
            "¿Continuar igualmente?");
        Add("confirm.unity_mismatch",
            "This file was built with:\n  {0}\n\n" +
            "This Editor is:\n  {1}\n\n" +
            "Important: open Unity {0} and run hotswap there instead.\n\n" +
            "Uploading a {0} world from Editor {1} is not recommended.\n" +
            "The upload might succeed, but joining the world usually will not work.\n\n" +
            "Other options:\n" +
            "- Use a .vrcw built with this Editor ({1})\n" +
            "- Or use a {2} .vrcw on Unity {2}\n\n" +
            "Continue anyway?",
            "这个文件是用以下版本构建的：\n  {0}\n\n" +
            "当前编辑器是：\n  {1}\n\n" +
            "重要：建议改用 Unity {0} 打开项目，在那里做热替换。\n\n" +
            "不建议在 {1} 里上传一个 {0} 的世界。\n" +
            "上传可能会成功，但一般进不去世界。\n\n" +
            "其他办法：\n" +
            "- 用当前编辑器（{1}）构建的 .vrcw\n" +
            "- 或者在 Unity {2} 里用 {2} 构建的 .vrcw\n\n" +
            "仍然继续吗？",
            "このファイルのビルド元：\n  {0}\n\n" +
            "このエディターは：\n  {1}\n\n" +
            "重要：Unity {0} でプロジェクトを開き、そちらでホットスワップすることをおすすめします。\n\n" +
            "エディター {1} から {0} のワールドをアップロードするのはおすすめしません。\n" +
            "アップロードは成功しても、ワールドに入れないことがほとんどです。\n\n" +
            "ほかの方法：\n" +
            "- このエディター（{1}）でビルドした .vrcw を使う\n" +
            "- または Unity {2} で {2} の .vrcw を使う\n\n" +
            "それでも続けますか？",
            "이 파일의 빌드 버전:\n  {0}\n\n" +
            "현재 에디터:\n  {1}\n\n" +
            "중요: Unity {0}에서 프로젝트를 열고 거기서 핫스왑하는 것을 권장합니다.\n\n" +
            "에디터 {1}에서 {0} 월드를 업로드하는 것은 권장하지 않습니다.\n" +
            "업로드는 성공해도 월드에 들어가지 못하는 경우가 많습니다.\n\n" +
            "다른 방법:\n" +
            "- 현재 에디터({1})로 빌드한 .vrcw 사용\n" +
            "- 또는 Unity {2}에서 {2} .vrcw 사용\n\n" +
            "그래도 계속할까요?",
            "Este archivo se compiló con:\n  {0}\n\n" +
            "Este Editor es:\n  {1}\n\n" +
            "Importante: mejor abre Unity {0} y haz el hotswap allí.\n\n" +
            "No se recomienda subir un mundo de {0} desde el Editor {1}.\n" +
            "La subida puede salir bien, pero normalmente no podrás entrar al mundo.\n\n" +
            "Otras opciones:\n" +
            "- Usa un .vrcw compilado con este Editor ({1})\n" +
            "- O usa un .vrcw de {2} en Unity {2}\n\n" +
            "¿Continuar igualmente?");
        Add("confirm.platform_ambiguous",
            "This file has mixed PC and Android markers.\n\nUnity is currently set to: {0}\n\nMake sure that matches your world, then continue.",
            "这个文件里同时有 PC 和 Android 的特征。\n\nUnity 当前的目标平台是：{0}\n\n确认它和你的世界一致后再继续。",
            "このファイルには PC と Android 両方の特徴があります。\n\nUnity の現在のターゲット：{0}\n\nワールドと一致しているか確認してから続けてください。",
            "이 파일에는 PC와 Android 특징이 함께 있습니다.\n\nUnity의 현재 타깃: {0}\n\n월드와 맞는지 확인한 뒤 계속하세요.",
            "Este archivo tiene marcas de PC y de Android a la vez.\n\nUnity está en: {0}\n\nAsegúrate de que coincide con tu mundo y continúa.");
        Add("confirm.platform_unknown",
            "Couldn't tell if this file is PC or Android.\n\nUnity is currently set to: {0}\n\nMake sure that matches your world, then continue.",
            "判断不出这个文件是 PC 还是 Android 的。\n\nUnity 当前的目标平台是：{0}\n\n确认它和你的世界一致后再继续。",
            "このファイルが PC 用か Android 用か判別できませんでした。\n\nUnity の現在のターゲット：{0}\n\nワールドと一致しているか確認してから続けてください。",
            "이 파일이 PC용인지 Android용인지 판별할 수 없습니다.\n\nUnity의 현재 타깃: {0}\n\n월드와 맞는지 확인한 뒤 계속하세요.",
            "No se pudo saber si este archivo es de PC o de Android.\n\nUnity está en: {0}\n\nAsegúrate de que coincide con tu mundo y continúa.");
        Add("confirm.platform_android_target_pc_file",
            "Unity is set to Android, but this file looks like a PC world.\n\nContinue anyway?",
            "Unity 当前的目标平台是 Android，但这个文件看起来是 PC 世界。\n\n仍然继续吗？",
            "Unity のターゲットは Android ですが、このファイルは PC のワールドのようです。\n\nそれでも続けますか？",
            "Unity 타깃은 Android이지만, 이 파일은 PC 월드로 보입니다.\n\n그래도 계속할까요?",
            "Unity está en Android, pero este archivo parece un mundo de PC.\n\n¿Continuar igualmente?");
        Add("confirm.platform_pc_target_android_file",
            "Unity is set to PC, but this file looks like an Android world.\n\nContinue anyway?",
            "Unity 当前的目标平台是 PC，但这个文件看起来是 Android 世界。\n\n仍然继续吗？",
            "Unity のターゲットは PC ですが、このファイルは Android のワールドのようです。\n\nそれでも続けますか？",
            "Unity 타깃은 PC이지만, 이 파일은 Android 월드로 보입니다.\n\n그래도 계속할까요?",
            "Unity está en PC, pero este archivo parece un mundo de Android.\n\n¿Continuar igualmente?");
        Add("confirm.pc_size_soft",
            "PC worlds over ~1 GB can get rejected.\n\nThis file is {0}.\n\nContinue anyway?",
            "超过约 1 GB 的 PC 世界有可能被拒。\n\n这个文件是 {0}。\n\n仍然继续吗？",
            "約 1 GB を超える PC ワールドは拒否されることがあります。\n\nこのファイルは {0} です。\n\nそれでも続けますか？",
            "약 1 GB를 넘는 PC 월드는 거부될 수 있습니다.\n\n이 파일은 {0}입니다.\n\n그래도 계속할까요?",
            "Los mundos de PC de más de ~1 GB pueden ser rechazados.\n\nEste archivo ocupa {0}.\n\n¿Continuar igualmente?");
        Add("confirm.pc_size_unlikely",
            "PC worlds this large are often rejected (~1.5-2.5 GB).\n\nThis file is {0}.\n\nContinue anyway?",
            "这么大的 PC 世界经常会被拒（约 1.5-2.5 GB）。\n\n这个文件是 {0}。\n\n仍然继续吗？",
            "このサイズの PC ワールドはよく拒否されます（約 1.5-2.5 GB）。\n\nこのファイルは {0} です。\n\nそれでも続けますか？",
            "이 정도 용량의 PC 월드는 자주 거부됩니다(약 1.5-2.5 GB).\n\n이 파일은 {0}입니다.\n\n그래도 계속할까요?",
            "Los mundos de PC de este tamaño se rechazan a menudo (~1.5-2.5 GB).\n\nEste archivo ocupa {0}.\n\n¿Continuar igualmente?");
        Add("confirm.pc_size_hopeless",
            "PC worlds over ~2.5 GB almost never upload.\n\nThis file is {0}.\n\nContinue anyway?",
            "超过约 2.5 GB 的 PC 世界基本上传不上去。\n\n这个文件是 {0}。\n\n仍然继续吗？",
            "約 2.5 GB を超える PC ワールドはほぼアップロードできません。\n\nこのファイルは {0} です。\n\nそれでも続けますか？",
            "약 2.5 GB를 넘는 PC 월드는 거의 업로드되지 않습니다.\n\n이 파일은 {0}입니다.\n\n그래도 계속할까요?",
            "Los mundos de PC de más de ~2.5 GB casi nunca se suben.\n\nEste archivo ocupa {0}.\n\n¿Continuar igualmente?");
        Add("confirm.android_size_try",
            "Android worlds must end up under 100 MB.\n\nThis file is {0}.\nPacking might shrink it enough. Want to try?",
            "Android 世界最后必须小于 100 MB。\n\n这个文件是 {0}。\n打包后也许能压到限制以内。要试试吗？",
            "Android のワールドは最終的に 100 MB 未満にする必要があります。\n\nこのファイルは {0} です。\nパックすれば収まるかもしれません。試しますか？",
            "Android 월드는 최종적으로 100 MB 미만이어야 합니다.\n\n이 파일은 {0}입니다.\n패킹하면 들어갈 수도 있습니다. 시도할까요?",
            "Los mundos de Android tienen que acabar por debajo de 100 MB.\n\nEste archivo ocupa {0}.\nEmpaquetarlo quizá lo reduzca lo suficiente. ¿Lo probamos?");
        Add("confirm.android_size_hopeless",
            "Android worlds must end up under 100 MB.\n\nThis file is {0}.\nOver 200 MB almost never works.\n\nContinue anyway?",
            "Android 世界最后必须小于 100 MB。\n\n这个文件是 {0}。\n超过 200 MB 基本没戏。\n\n仍然继续吗？",
            "Android のワールドは最終的に 100 MB 未満にする必要があります。\n\nこのファイルは {0} です。\n200 MB を超えるとほぼ成功しません。\n\nそれでも続けますか？",
            "Android 월드는 최종적으로 100 MB 미만이어야 합니다.\n\n이 파일은 {0}입니다.\n200 MB를 넘으면 거의 성공하지 않습니다.\n\n그래도 계속할까요?",
            "Los mundos de Android tienen que acabar por debajo de 100 MB.\n\nEste archivo ocupa {0}.\nPor encima de 200 MB casi nunca funciona.\n\n¿Continuar igualmente?");

        return map;
    }
}

public class VRCWorldHotswapLanguagePicker : EditorWindow
{
    private VRCWLocLang? picked;
    private GUIStyle bodyStyle;
    private GUIStyle footerStyle;

    public static VRCWLocLang? Prompt()
    {
        var window = CreateInstance<VRCWorldHotswapLanguagePicker>();
        window.titleContent = new GUIContent(VRCWorldHotswapLoc.T("lang.picker.title"));
        window.minSize = new Vector2(520, 460);
        window.maxSize = new Vector2(520, 460);
        window.ShowModalUtility();
        return window.picked;
    }

    private void OnGUI()
    {
        if (bodyStyle == null)
        bodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };

        if (footerStyle == null)
        footerStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

        GUILayout.Space(8);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("app.name"), EditorStyles.boldLabel);
        GUILayout.Space(2);
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("lang.picker.body"), bodyStyle, GUILayout.Height(88));
        GUILayout.Space(4);

        foreach (VRCWLocLang language in Enum.GetValues(typeof(VRCWLocLang)))
        {
            if (GUILayout.Button(VRCWorldHotswapLoc.NativeName(language), GUILayout.Height(30)))
            {
                picked = language;
                Close();
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(VRCWorldHotswapLoc.T("lang.picker.footer"), footerStyle, GUILayout.Height(84));
        GUILayout.Space(4);
    }
}
#endif
