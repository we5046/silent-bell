namespace SilentBell.Server;

// 서버 로그는 이 형식 하나로 표준 출력에 쓴다. systemd가 journalctl로 모은다.
// 2026-09-26 21:04:15.018 [INFO ] session    7 connected 1.2.3.4:51234
public static class Log
{
    public static void Info(string category, string message) => Write("INFO ", category, message);
    public static void Warn(string category, string message) => Write("WARN ", category, message);
    public static void Error(string category, string message) => Write("ERROR", category, message);

    static void Write(string level, string category, string message) =>
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category,-10} {message}");
}
