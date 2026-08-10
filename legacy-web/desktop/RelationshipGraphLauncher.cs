using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("关系图编辑器")]
[assembly: AssemblyDescription("关系图编辑器 Windows 桌面启动器")]
[assembly: AssemblyCompany("Relationship Studio")]
[assembly: AssemblyProduct("关系图编辑器")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.3.9.0")]
[assembly: AssemblyFileVersion("1.3.9.0")]

namespace RelationshipGraphEditor
{
    internal static class Program
    {
        private const int DesktopPort = 17653;

        [STAThread]
        private static void Main(string[] arguments)
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            bool serverOnly = Array.Exists(arguments, delegate(string value)
            {
                return String.Equals(value, "--server-only", StringComparison.OrdinalIgnoreCase);
            });

            DesktopWebServer webServer = null;
            bool ownsServer = false;
            try
            {
                webServer = new DesktopWebServer(DesktopPort);
                ownsServer = webServer.TryStart();

                if (!ownsServer && !DesktopWebServer.IsRelationshipStudioServerRunning(DesktopPort))
                {
                    ShowError("桌面服务端口被占用", "本机端口 17653 已被其他程序占用。关闭占用程序后再试，或重新启动电脑。");
                    return;
                }

                string appUrl = String.Format("http://127.0.0.1:{0}/index.html?desktop=1", DesktopPort);
                if (!serverOnly)
                {
                    string browserPath = FindChromiumBrowser();
                    if (String.IsNullOrEmpty(browserPath))
                    {
                        ShowError("缺少桌面运行环境", "未找到 Microsoft Edge 或 Google Chrome。安装其中任意一个浏览器后，即可用桌面窗口运行关系图编辑器。");
                        return;
                    }

                    LaunchDesktopWindow(browserPath, appUrl, appDirectory);
                }

                if (ownsServer) webServer.WaitUntilInactive();
            }
            catch (Exception error)
            {
                if (!serverOnly) ShowError("无法启动关系图编辑器", "桌面窗口启动失败：" + error.Message);
            }
            finally
            {
                if (webServer != null) webServer.Dispose();
            }
        }

        private static void LaunchDesktopWindow(string browserPath, string appUrl, string appDirectory)
        {
            string browserProfileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RelationshipGraphEditor",
                "BrowserProfile"
            );
            Directory.CreateDirectory(browserProfileDirectory);
            string arguments = String.Format(
                "--app=\"{0}\" --user-data-dir=\"{1}\" --start-maximized --no-first-run --no-default-browser-check " +
                "--disable-extensions --disable-pinch --overscroll-history-navigation=0 " +
                "--disable-features=msEdgeSidebarV2,msEdgeMouseGesture,msEdgeMouseGestureDefaultEnabled,TouchpadOverscrollHistoryNavigation",
                appUrl,
                browserProfileDirectory
            );

            Process.Start(new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = arguments,
                WorkingDirectory = appDirectory,
                UseShellExecute = false
            });
        }

        private static string FindChromiumBrowser()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new List<string>
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (string candidate in candidates)
            {
                if (!String.IsNullOrEmpty(candidate) && File.Exists(candidate)) return candidate;
            }

            return FindOnPath("chrome.exe") ?? FindOnPath("msedge.exe");
        }

        private static string FindOnPath(string executableName)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (String.IsNullOrWhiteSpace(directory)) continue;
                try
                {
                    string candidate = Path.Combine(directory.Trim(), executableName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries and continue with the next one.
                }
            }
            return null;
        }

        private static void ShowError(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class DesktopWebServer : IDisposable
    {
        private const string ServerSignature = "relationship-studio-desktop";
        private const string ResourcePrefix = "RelationshipGraphEditor.Web.";
        private static readonly Dictionary<string, EmbeddedWebFile> EmbeddedFiles =
            new Dictionary<string, EmbeddedWebFile>(StringComparer.OrdinalIgnoreCase)
            {
                { "/index.html", new EmbeddedWebFile(ResourcePrefix + "index.html", "text/html; charset=utf-8") },
                { "/app.css", new EmbeddedWebFile(ResourcePrefix + "app.css", "text/css; charset=utf-8") },
                { "/app.js", new EmbeddedWebFile(ResourcePrefix + "app.js", "application/javascript; charset=utf-8") },
                { "/system-function-default.js", new EmbeddedWebFile(ResourcePrefix + "system-function-default.js", "application/javascript; charset=utf-8") },
                { "/node-type-core.js", new EmbeddedWebFile(ResourcePrefix + "node-type-core.js", "application/javascript; charset=utf-8") },
                { "/routing-core.js", new EmbeddedWebFile(ResourcePrefix + "routing-core.js", "application/javascript; charset=utf-8") },
                { "/project-store.js", new EmbeddedWebFile(ResourcePrefix + "project-store.js", "application/javascript; charset=utf-8") },
                { "/readonly-export.js", new EmbeddedWebFile(ResourcePrefix + "readonly-export.js", "application/javascript; charset=utf-8") },
                { "/import-core.js", new EmbeddedWebFile(ResourcePrefix + "import-core.js", "application/javascript; charset=utf-8") },
                { "/assets/app-icon.svg", new EmbeddedWebFile(ResourcePrefix + "assets.app-icon.svg", "image/svg+xml") }
            };
        private readonly int port;
        private TcpListener listener;
        private Thread listenerThread;
        private volatile bool stopRequested;
        private volatile bool heartbeatSeen;
        private DateTime lastHeartbeatUtc;
        private readonly DateTime startedUtc;

        public DesktopWebServer(int port)
        {
            this.port = port;
            this.startedUtc = DateTime.UtcNow;
            this.lastHeartbeatUtc = this.startedUtc;
        }

        public bool TryStart()
        {
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(16);
                listenerThread = new Thread(ListenLoop);
                listenerThread.IsBackground = true;
                listenerThread.Name = "Relationship Studio local server";
                listenerThread.Start();
                return true;
            }
            catch (SocketException)
            {
                if (listener != null) listener.Stop();
                listener = null;
                return false;
            }
        }

        public void WaitUntilInactive()
        {
            while (!stopRequested)
            {
                Thread.Sleep(1000);
                DateTime now = DateTime.UtcNow;
                if (heartbeatSeen && now.Subtract(lastHeartbeatUtc).TotalSeconds > 90) break;
                if (!heartbeatSeen && now.Subtract(startedUtc).TotalSeconds > 120) break;
            }
        }

        public static bool IsRelationshipStudioServerRunning(int port)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    String.Format("http://127.0.0.1:{0}/__health", port)
                );
                request.Method = "GET";
                request.Timeout = 700;
                request.ReadWriteTimeout = 700;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    return String.Equals(reader.ReadToEnd(), ServerSignature, StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }

        private void ListenLoop()
        {
            while (!stopRequested && listener != null)
            {
                try
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(35);
                        continue;
                    }
                    using (TcpClient client = listener.AcceptTcpClient()) HandleClient(client);
                }
                catch (SocketException)
                {
                    if (!stopRequested) Thread.Sleep(80);
                }
                catch
                {
                    // One malformed request must not stop the local desktop server.
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            client.ReceiveTimeout = 2500;
            client.SendTimeout = 2500;
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
            {
                string requestLine = reader.ReadLine();
                if (String.IsNullOrWhiteSpace(requestLine)) return;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string headerLine;
                while (!String.IsNullOrEmpty(headerLine = reader.ReadLine()))
                {
                    int separatorIndex = headerLine.IndexOf(':');
                    if (separatorIndex <= 0) continue;
                    string name = headerLine.Substring(0, separatorIndex).Trim();
                    string value = headerLine.Substring(separatorIndex + 1).Trim();
                    if (name.Length > 0) headers[name] = value;
                }

                string[] parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    WriteResponse(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad request"), false);
                    return;
                }

                string method = parts[0].ToUpperInvariant();
                string rawPath = parts[1];
                int queryIndex = rawPath.IndexOf('?');
                if (queryIndex >= 0) rawPath = rawPath.Substring(0, queryIndex);

                if (rawPath == "/__health")
                {
                    if (method != "GET" && method != "HEAD")
                    {
                        WriteResponse(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed"), false);
                        return;
                    }
                    WriteResponse(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ServerSignature), method == "HEAD");
                    return;
                }

                if (rawPath == "/__heartbeat")
                {
                    if (method != "POST")
                    {
                        WriteResponse(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed"), false);
                        return;
                    }
                    if (!IsAllowedLocalOrigin(headers))
                    {
                        WriteResponse(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Forbidden"), false);
                        return;
                    }
                    heartbeatSeen = true;
                    lastHeartbeatUtc = DateTime.UtcNow;
                    WriteResponse(stream, 204, "text/plain", new byte[0], false);
                    return;
                }

                if (rawPath == "/__shutdown" && method == "POST")
                {
                    if (!IsAllowedLocalOrigin(headers))
                    {
                        WriteResponse(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Forbidden"), false);
                        return;
                    }
                    WriteResponse(stream, 204, "text/plain", new byte[0], false);
                    stopRequested = true;
                    return;
                }

                if (method != "GET" && method != "HEAD")
                {
                    WriteResponse(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed"), false);
                    return;
                }

                ServeEmbeddedFile(stream, rawPath, method == "HEAD");
            }
        }

        private bool IsAllowedLocalOrigin(Dictionary<string, string> headers)
        {
            string origin;
            if (!headers.TryGetValue("Origin", out origin) || String.IsNullOrWhiteSpace(origin)) return true;
            string expectedOrigin = String.Format("http://127.0.0.1:{0}", port);
            return String.Equals(origin.TrimEnd('/'), expectedOrigin, StringComparison.OrdinalIgnoreCase);
        }

        private void ServeEmbeddedFile(NetworkStream stream, string rawPath, bool headOnly)
        {
            string decodedPath;
            try
            {
                decodedPath = Uri.UnescapeDataString(rawPath);
            }
            catch
            {
                WriteResponse(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad path"), headOnly);
                return;
            }

            if (decodedPath == "/") decodedPath = "/index.html";
            decodedPath = decodedPath.Replace('\\', '/');
            EmbeddedWebFile embeddedFile;
            if (!EmbeddedFiles.TryGetValue(decodedPath, out embeddedFile))
            {
                WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"), headOnly);
                return;
            }

            byte[] content;
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resourceStream = assembly.GetManifestResourceStream(embeddedFile.ResourceName))
            {
                if (resourceStream == null)
                {
                    WriteResponse(stream, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Embedded application resource is missing"), headOnly);
                    return;
                }
                using (MemoryStream memory = new MemoryStream())
                {
                    resourceStream.CopyTo(memory);
                    content = memory.ToArray();
                }
            }
            WriteResponse(stream, 200, embeddedFile.ContentType, content, headOnly);
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string contentType, byte[] content, bool headOnly)
        {
            string statusText = statusCode == 200 ? "OK" :
                statusCode == 204 ? "No Content" :
                statusCode == 400 ? "Bad Request" :
                statusCode == 403 ? "Forbidden" :
                statusCode == 404 ? "Not Found" :
                statusCode == 500 ? "Internal Server Error" : "Method Not Allowed";
            string headers = String.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: {2}\r\nContent-Length: {3}\r\nCache-Control: no-cache\r\n" +
                "Content-Security-Policy: default-src 'self'; script-src 'self' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; worker-src blob:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'\r\n" +
                "Cross-Origin-Resource-Policy: same-origin\r\nPermissions-Policy: camera=(), microphone=(), geolocation=()\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nConnection: close\r\n\r\n",
                statusCode,
                statusText,
                contentType,
                content.Length
            );
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (!headOnly && content.Length > 0) stream.Write(content, 0, content.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            stopRequested = true;
            if (listener != null)
            {
                try { listener.Stop(); }
                catch { }
                listener = null;
            }
            if (listenerThread != null && listenerThread.IsAlive) listenerThread.Join(500);
        }
    }

    internal sealed class EmbeddedWebFile
    {
        public readonly string ResourceName;
        public readonly string ContentType;

        public EmbeddedWebFile(string resourceName, string contentType)
        {
            ResourceName = resourceName;
            ContentType = contentType;
        }
    }
}
