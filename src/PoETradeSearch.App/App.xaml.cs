using System.Windows;
using PoETradeSearch.App.Services;

namespace PoETradeSearch.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Anything that would crash the app goes to the log first, so it can be diagnosed on another PC.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("처리되지 않은 오류 (UI)", args.Exception);
            MessageBox.Show("오류가 발생했습니다. 메인 창의 '진단 정보 복사'로 내용을 보내주세요.\n\n" + args.Exception.Message,
                            "PoE Trade Search", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("처리되지 않은 오류", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("처리되지 않은 비동기 오류", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}
