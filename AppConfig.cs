using System;

namespace uhr.info.detector
{
    /// <summary>
    /// アプリケーション設定定数クラス
    /// </summary>
    public static class AppConfig
    {
        #region SVN 設定
        /// <summary>
        /// SVNサーバのベースURL
        /// </summary>
        public const string SVN_SERVER_BASE = "http://192.168.21.111/svn";
        
        /// <summary>
        /// カスタマイズデータのSVNパス
        /// </summary>
        public const string SVN_CUSTOMIZED_PATH = SVN_SERVER_BASE + "/UHR_CUSTOMIZED/";
        
        /// <summary>
        /// リリース成果物のSVNパス
        /// </summary>
        public const string SVN_RELEASE_ARTIFACTS_PATH = SVN_SERVER_BASE + "/nho-hospnet3/trunk/doc/06.UHR/99.リリース成果物";
        
        /// <summary>
        /// SVN実行ファイルの相対パス
        /// </summary>
        public const string SVN_EXE_RELATIVE_PATH = "svn\\svn.exe";
        
        /// <summary>
        /// SVNコマンドのタイムアウト時間（ミリ秒）
        /// </summary>
        public const int SVN_COMMAND_TIMEOUT_MS = 3000;
        #endregion

        #region データベース設定
        /// <summary>
        /// Oracle データベース接続文字列
        /// </summary>
        public const string ORACLE_CONNECTION_STRING = "User Id=UHRHANS;Password=HANS;Data Source=192.168.22.103:1521/SJK";
        #endregion

        #region バージョン設定
        /// <summary>
        /// 固定FWターゲットバージョン
        /// </summary>
        public const string FIXED_FW_TARGET_VERSION = "4.18.4";
        
        /// <summary>
        /// バージョンプレフィックス（除去用）
        /// </summary>
        public const string VERSION_PREFIX = "V";
        #endregion

        #region UI設定
        /// <summary>
        /// リストボックスの初期選択インデックス
        /// </summary>
        public const int DEFAULT_SELECTED_INDEX = 0;
        
        /// <summary>
        /// フィルタ適用時の最初のアイテム選択インデックス
        /// </summary>
        public const int FIRST_ITEM_INDEX = 0;
        #endregion

        #region ファイル・フォルダ設定
        /// <summary>
        /// HRデータベースフォルダサフィックス
        /// </summary>
        public const string HRDB_FOLDER_SUFFIX = "_HRDB/";
        
        /// <summary>
        /// 共通機能フォルダ名キーワード
        /// </summary>
        public const string COMMON_FUNCTION_FOLDER_KEYWORD = "(U-PDS HR 共通機能)";
        
        /// <summary>
        /// Web給与明細フォルダ名キーワード
        /// </summary>
        public const string WEB_SALARY_FOLDER_KEYWORD = "(U-PDS HR Web給与明細)";
        
        /// <summary>
        /// 年末調整フォルダ名キーワード
        /// </summary>
        public const string YEAR_ADJUST_FOLDER_KEYWORD = "(U-PDS HR 年末調整)";
        
        /// <summary>
        /// Coreモジュールパス
        /// </summary>
        public const string CORE_MODULE_PATH = "/cd/U-PDS_HR_COMMON/モジュール/アップグレードユーザ向け";
        
        /// <summary>
        /// Salaryモジュールパス
        /// </summary>
        public const string SALARY_MODULE_PATH = "/cd/U-PDS_HR_SALARY/モジュール/アップグレードユーザ向け";
        
        /// <summary>
        /// Nenchoモジュールパス
        /// </summary>
        public const string YEAR_ADJUST_MODULE_PATH = "/cd/U-PDS_HR_NENCHO/モジュール/アップグレードユーザ向け";
        
        /// <summary>
        /// SQLファイルパス形式
        /// </summary>
        public const string SQL_FILE_PATH_FORMAT = "/ddl_and_master_data/MASTER_DATA/CONF_SYSCONTROL.SQL";

        /// <summary>
        /// UHRモジュールパス形式
        /// </summary>
        public const string UHR_MODULE_PATH = "/uhr";
        
        /// <summary>
        /// 代码基路径（用于文件浏览器）
        /// </summary>
        public const string CODE_BASE_PATH = "C:\\";
        #endregion

        #region Oracle エラーコード
        /// <summary>
        /// Oracle テーブルまたはビューが存在しないエラーコード
        /// </summary>
        public const string ORACLE_ERROR_TABLE_NOT_EXISTS = "ORA-00942";
        
        /// <summary>
        /// Oracle オブジェクトが存在しないエラーコード
        /// </summary>
        public const string ORACLE_ERROR_OBJECT_NOT_EXISTS = "ORA-04043";
        #endregion

        #region メッセージ
        /// <summary>
        /// SVN実行ファイル未発見エラーメッセージ
        /// </summary>
        public const string MSG_SVN_EXE_NOT_FOUND = "svn.exe が見つかりません。svn フォルダが正しく配置されているか確認してください。";
        
        /// <summary>
        /// SVNプロセス起動失敗エラーメッセージ
        /// </summary>
        public const string MSG_SVN_PROCESS_START_FAILED = "svn プロセスの起動に失敗しました。";
        
        /// <summary>
        /// SVNコマンド実行失敗エラーメッセージ
        /// </summary>
        public const string MSG_SVN_COMMAND_FAILED = "svn コマンドの実行に失敗しました（プロセスがすぐに終了しました）。\n\n";
        
        /// <summary>
        /// SVNコマンド実行時エラーメッセージ
        /// </summary>
        public const string MSG_SVN_COMMAND_ERROR = "svn コマンド実行時にエラーが発生しました:\n\n";
        
        /// <summary>
        /// 依存関係不足エラーメッセージ
        /// </summary>
        public const string MSG_DEPENDENCY_MISSING = "外部コマンド（例: svn.exe）が見つかりません。SVN がインストールされ、PATH に追加されているか確認してください。\n\n詳細: ";
        
        /// <summary>
        /// 予期しないエラーメッセージ
        /// </summary>
        public const string MSG_UNEXPECTED_ERROR = "予期しないエラーが発生しました:\n\n";
        
        /// <summary>
        /// Coreバージョン取得失敗メッセージ
        /// </summary>
        public const string MSG_CORE_VERSION_GET_FAILED = "Core バージョン情報の取得に失敗しました: ";
        
        /// <summary>
        /// Salaryバージョン取得失敗メッセージ
        /// </summary>
        public const string MSG_SALARY_VERSION_GET_FAILED = "Salary バージョン情報の取得に失敗しました: ";
        
        /// <summary>
        /// Nenchoバージョン取得失敗メッセージ
        /// </summary>
        public const string MSG_YEAR_ADJUST_VERSION_GET_FAILED = "Nencho バージョン情報の取得に失敗しました: ";
        
        /// <summary>
        /// カスタマイズファイル取得失敗メッセージ
        /// </summary>
        public const string MSG_CUSTOMIZED_FILE_GET_FAILED = "カスタマイズファイルの取得に失敗しました: ";
        
        /// <summary>
        /// ファイル比較失敗メッセージ
        /// </summary>
        public const string MSG_FILE_COMPARE_FAILED = "ファイル比較処理中にエラーが発生しました: ";
        
        /// <summary>
        /// SVNサブディレクトリ取得失敗メッセージ
        /// </summary>
        public const string MSG_SVN_SUBDIRECTORY_GET_FAILED = "SVN サブディレクトリの取得に失敗しました: ";
        
        /// <summary>
        /// SVNファイルリスト取得失敗メッセージ
        /// </summary>
        public const string MSG_SVN_FILE_LIST_GET_FAILED = "SVN ファイルリストの取得に失敗しました: ";
        #endregion

        #region タイトル
        /// <summary>
        /// 依存関係不足タイトル
        /// </summary>
        public const string TITLE_DEPENDENCY_MISSING = "依存関係が不足";
        
        /// <summary>
        /// SVNエラータイトル
        /// </summary>
        public const string TITLE_SVN_ERROR = "SVNエラー";
        
        /// <summary>
        /// エラータイトル
        /// </summary>
        public const string TITLE_ERROR = "エラー";
        
        /// <summary>
        /// 警告タイトル
        /// </summary>
        public const string TITLE_WARNING = "警告";
        #endregion
    }
}