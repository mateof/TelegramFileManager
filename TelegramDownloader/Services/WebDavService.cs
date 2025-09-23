namespace TelegramDownloader.Services
{
    using System.Diagnostics;

    public class WebbDavService
    {
        private static Process? _pythonProcess;

        public void Start(string scriptPath = "WebDav/webdav_api_proxy.py", int port = 8000, int externalPort = 9081, string host = "127.0.0.1")
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
                return; // ya está corriendo

            var startInfo = new ProcessStartInfo
            {
                FileName = "python", // o "python3" según tu sistema
                Arguments = $"{scriptPath} --port {port} --out-port {externalPort} --host {host}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _pythonProcess = new Process { StartInfo = startInfo };
            _pythonProcess.OutputDataReceived += (s, e) => Console.WriteLine(e.Data);
            _pythonProcess.ErrorDataReceived += (s, e) => Console.WriteLine(e.Data);

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();
        }

        public void Stop()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _pythonProcess.Kill();
                _pythonProcess.Dispose();
                _pythonProcess = null;
            }
        }

        public void Restart(string scriptPath, int port = 8000)
        {
            Stop();
            Start(scriptPath, port);
        }

        public bool IsRunning => _pythonProcess != null && !_pythonProcess.HasExited;
    }
}
