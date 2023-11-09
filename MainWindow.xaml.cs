using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NMC_Launcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region DllImports
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(int hProcess, int lpBaseAddress, byte[] buffer, int size, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(int hProcess, int lpBaseAddress, byte[] buffer, int size, out int lpNumberOfBytesWritten);
        #endregion

        bool fullscreen = false;
        bool gameWindowOnly = false;
        bool hasVolumeSaver = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // PCSX2 Launch Settings
            Process ps2 = new();

            ps2.StartInfo.FileName = "..\\pcsx2-v1.7.2746-windows-x86\\pcsx2x64.exe";
            ps2.StartInfo.Arguments = "\"..\\Patcher\\KH2FM.NEW.ISO\"";

            if (!File.Exists("..\\Patcher\\KH2FM.NEW.ISO"))
                MessageBox.Show("KH2FM.NEW.ISO not found. Please make sure the launcher is located in the Music Tool folder.", "KH2FM.NEW.ISO Not Found");

            // Check if fullscreen/game window only is checked
            if (fullscreen)
            {
                ps2.StartInfo.Arguments += " --fullscreen";
            }
            if (gameWindowOnly)
            {
                ps2.StartInfo.Arguments += " --nogui";
            }

            try
            {
                ps2.Start();
            }
            catch
            {
                MessageBox.Show("PCSX2 not found. Please make sure the launcher is located in the Music Tool folder.");
            }

            // Music Launch Settings
            Process music = new();
            if (hasVolumeSaver)
            {
                music.StartInfo.FileName = ".\\music_gui_volume.exe";
                try
                {
                    music.Start();
                }
                catch
                {
                    MessageBox.Show("Music GUI Volume Saver not found. Please make sure the launcher is located in the Music Tool folder.", "Music GUI Volume Saver Not Found");
                }
            }
            else
            {
                music.StartInfo.FileName = ".\\Music_GUI.exe";
                try
                {
                    music.Start();
                }
                catch
                {
                    MessageBox.Show("Music GUI not found. Please make sure the launcher is located in the Music Tool folder.", "Music GUI Not Found");
                }
            }

            //Volume_Saver();

            // Puts PCSX2 in foreground after Music_GUI launches
            while (true)
            {
                Thread.Sleep(100);
                IntPtr hWnd = FindWindow(null, "Custom Music Tool (Xaddgx, GovanifY)");
                if (hWnd != IntPtr.Zero)
                {
                    SetForegroundWindow(ps2.MainWindowHandle);
                    break;
                }
            }

            // Closes launcher
            Environment.Exit(0);

        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            if (!File.Exists(".\\music_gui_volume.exe"))
            {
                hasVolumeSaver = false;
                MessageBoxResult result = MessageBox.Show("Volume Saver not found. Download Volume Saver?\n\nNote: Without it, you will need to adjust the volume each time Music_GUI.exe launches if you changed the volume level.", "Music GUI Volume Saver Not Found", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    WebClient webClient = new();
                    try
                    {
                        webClient.DownloadFile("https://github.com/benr0th/Music-GUI-Volume-Saver/releases/download/latest/music_gui_volume.exe", "music_gui_volume.exe");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    finally
                    {
                        webClient.Dispose();
                        MessageBox.Show("Volume Saver downloaded. Press Play to start the game.", "Volume Saver Downloaded");
                        hasVolumeSaver = true;
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    hasVolumeSaver = false;
                }
            }
            else
                hasVolumeSaver = true;
        }

        private void CheckBoxFS_Checked(object sender, RoutedEventArgs e)
        {
            fullscreen = true;
        }

        private void CheckBoxFS_Unchecked(object sender, RoutedEventArgs e)
        {
            fullscreen = false;
        }

        private void CheckBoxGW_Checked(object sender, RoutedEventArgs e)
        {
            gameWindowOnly = true;
        }

        private void CheckBoxGW_Unchecked(object sender, RoutedEventArgs e)
        {
            gameWindowOnly = false;
        }

        private void Volume_Saver()
        {
            var Found = false;

            while (!Found)
            {
                Thread.Sleep(250);
                Found = Memory.Attatch("Music_GUI");
            }


            Process[] processes = Process.GetProcessesByName("Music_GUI");
            int BaseAddress = processes[0].MainModule.BaseAddress.ToInt32();
            int volume = Memory.ReadMemory<int>(BaseAddress + 0x251010);
            MessageBox.Show(volume.ToString());


            // Get volume
            //int volume = Memory.ReadMemory<int>(BaseAddress + 0x251010);
            // Save volume
            //Properties.Settings.Default.Volume = volume;
            //Properties.Settings.Default.Save();
        }

        // This is for future internal volume saver use
        internal class Memory
        {
            private static Process? m_iProcess;
            private static IntPtr m_iProcessHandle;

            private static int m_iBytesWritten;
            private static int m_iBytesRead;

            public static bool Attatch(string ProcName)
            {
                if (Process.GetProcessesByName(ProcName).Length > 0)
                {
                    m_iProcess = Process.GetProcessesByName(ProcName)[0];
                    m_iProcessHandle =
                        OpenProcess(Flags.PROCESS_VM_OPERATION | Flags.PROCESS_VM_READ | Flags.PROCESS_VM_WRITE,
                            false, m_iProcess.Id);
                    return true;
                }

                return false;
            }

            public static void WriteMemory<T>(int Address, object Value)
            {
                var buffer = StructureToByteArray(Value);

                WriteProcessMemory((int)m_iProcessHandle, Address, buffer, buffer.Length, out m_iBytesWritten);
            }

            public static void WriteMemory<T>(int Adress, char[] Value)
            {
                var buffer = Encoding.UTF8.GetBytes(Value);

                WriteProcessMemory((int)m_iProcessHandle, Adress, buffer, buffer.Length, out m_iBytesWritten);
            }

            public static T ReadMemory<T>(int address) where T : struct
            {
                var ByteSize = Marshal.SizeOf(typeof(T));

                var buffer = new byte[ByteSize];

                ReadProcessMemory((int)m_iProcessHandle, address, buffer, buffer.Length, ref m_iBytesRead);

                return ByteArrayToStructure<T>(buffer);
            }

            public static byte[] ReadMemory(int offset, int size)
            {
                var buffer = new byte[size];

                ReadProcessMemory((int)m_iProcessHandle, offset, buffer, size, ref m_iBytesRead);

                return buffer;
            }

            public static float[] ReadMatrix<T>(int Adress, int MatrixSize) where T : struct
            {
                var ByteSize = Marshal.SizeOf(typeof(T));
                var buffer = new byte[ByteSize * MatrixSize];
                ReadProcessMemory((int)m_iProcessHandle, Adress, buffer, buffer.Length, ref m_iBytesRead);

                return ConvertToFloatArray(buffer);
            }

            public static int GetModuleAddress(string Name)
            {
                try
                {
                    foreach (ProcessModule ProcMod in m_iProcess.Modules)
                        if (Name == ProcMod.ModuleName)
                            return (int)ProcMod.BaseAddress;
                }
                catch
                {
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: Cannot find - " + Name + " | Check file extension.");
                Console.ResetColor();

                return -1;
            }

            #region Other

            internal struct Flags
            {
                public const int PROCESS_VM_OPERATION = 0x0008;
                public const int PROCESS_VM_READ = 0x0010;
                public const int PROCESS_VM_WRITE = 0x0020;
            }

            #endregion

            #region Conversion

            public static float[] ConvertToFloatArray(byte[] bytes)
            {
                if (bytes.Length % 4 != 0)
                    throw new ArgumentException();

                var floats = new float[bytes.Length / 4];

                for (var i = 0; i < floats.Length; i++)
                    floats[i] = BitConverter.ToSingle(bytes, i * 4);

                return floats;
            }

            private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
            {
                var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
                }
                finally
                {
                    handle.Free();
                }
            }

            private static byte[] StructureToByteArray(object obj)
            {
                var length = Marshal.SizeOf(obj);

                var array = new byte[length];

                var pointer = Marshal.AllocHGlobal(length);

                Marshal.StructureToPtr(obj, pointer, true);
                Marshal.Copy(pointer, array, 0, length);
                Marshal.FreeHGlobal(pointer);

                return array;
            }

            #endregion
        }
    }
}
