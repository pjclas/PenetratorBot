using PS4RemotePlayInterceptor;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PenetratorBot
{
    internal class Program
    {
        private enum ProcessDPIAwareness
        {
            ProcessDPIUnaware = 0,
            ProcessSystemDPIAware = 1,
            ProcessPerMonitorDPIAware = 2
        }
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(ProcessDPIAwareness value);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(System.Int32 vKey);

        private const int QUIT_KEY = 0x51;  // 'Q' key

        static void Main(string[] args)
        {
            // set DPI awareness to handle proper screen resolution if we are zoomed in windows
            SetProcessDpiAwareness(ProcessDPIAwareness.ProcessPerMonitorDPIAware);

            // create new penetrator worker bot
            Penetrator penetrator = new Penetrator();

            // Setup callback to interceptor
            Interceptor.Callback = new InterceptionDelegate(penetrator.OnReceiveData);
            Interceptor.EmulateController = false;
            Interceptor.InjectionMode = InjectionMode.Compatibility;

            Process remotePlayProcess;
            // Attempt to inject into PS Remote Play
            try
            {
                Console.WriteLine("Press 'q' to quit program.");

                // Inject
                int pid = Interceptor.Inject();
                remotePlayProcess = Process.GetProcessById(pid);
                IntPtr handle = remotePlayProcess.MainWindowHandle;
                SetForegroundWindow(handle);

                // create a thread to play the game
                Thread gameThread = new Thread(penetrator.PlayGame);
                gameThread.Start();

                int quitKey = 0;
                while (gameThread.IsAlive)
                {
                    // Press 'Q' key to quit game
                    quitKey = GetAsyncKeyState(QUIT_KEY);

                    if ((quitKey & 0x01) == 0x01)
                    {
                        Console.WriteLine("Quit key pressed exiting program...");
                        break;
                    }
                    Thread.Sleep(100);
                }

                // end penetrator bot
                penetrator.EndProgram = true;

                // wait for our game thread to end
                gameThread.Join();
            }
            // Injection failed
            catch (InterceptorException ex)
            {
                // Only handle when PS4 Remote Play is in used by another injection
                if (ex.InnerException != null && ex.InnerException.Message.Equals("STATUS_INTERNAL_ERROR: Unknown error in injected C++ completion routine. (Code: 15)"))
                {
                    MessageBox.Show("The process has been injected by another executable. Restart PS Remote Play and try again.", "Injection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(-1);
                }
                else
                {
                    // Handle other exceptions
                    MessageBox.Show(string.Format("[{0}] - {1}", ex.GetType(), ex.Message), "Injection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(-1);
                }
            }
        }
    }
}
