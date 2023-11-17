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
using IniParser;
using IniParser.Model;
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

        public bool Fullscreen { get; set; }
        public bool GameWindowOnly { get; set; }
        public bool SkipLauncher { get; set; }
        bool Found = false;
        bool hadVolumeIni = false;
        int volume;
        int new_volume;
        string version;

        FileIniDataParser parser = new();
        IniData data = new();

        private void BuildConfig()
        {
            if (File.Exists("volume.ini"))
            {
                volume = int.Parse(File.ReadAllText("volume.ini"));
                hadVolumeIni = true;
                File.Delete("volume.ini");
            }
            
            if (!File.Exists("config.ini"))
            {
                Fullscreen = true;
                GameWindowOnly = true;
                SkipLauncher = false;
                data["Settings"]["Fullscreen"] = "True";
                data["Settings"]["GameWindowOnly"] = "True";
                data["Settings"]["SkipLauncher"] = "False";
                data["Music GUI"]["Volume"] = hadVolumeIni ? volume.ToString() : "50";
                parser.WriteFile("config.ini", data);
            } else
            {
                IniData data = parser.ReadFile("config.ini");
                Fullscreen = bool.Parse(data["Settings"]["Fullscreen"]);
                GameWindowOnly = bool.Parse(data["Settings"]["GameWindowOnly"]);
                SkipLauncher = bool.Parse(data["Settings"]["SkipLauncher"]);
                volume = int.Parse(data["Music GUI"]["Volume"]);
            }
        }

        public MainWindow()
        {
            BuildConfig();
            InitializeComponent();

            DataContext = this;

            if (SkipLauncher)
            {
                PlayButton_Click(null, null);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // PCSX2 Launch Settings
            Process ps2 = new();

            if (File.Exists("..\\pcsx2-v1.7.2746-windows-x86\\pcsx2x64.exe"))
            {
                version = "Rev 5 and below";
                ps2.StartInfo.FileName = "..\\pcsx2-v1.7.2746-windows-x86\\pcsx2x64.exe";
            }
            else if (File.Exists("..\\PCSX2 NMC Executable\\pcsx2x64.exe"))
            {
                version = "Rev 6+";
                ps2.StartInfo.FileName = "..\\PCSX2 NMC Executable\\pcsx2x64.exe";
            }
            else
            {
                MessageBox.Show("PCSX2 not found. Please make sure the launcher is located in the Music Tool folder.", "PCSX2 Not Found");
            }

            ps2.StartInfo.Arguments = "\"..\\Patcher\\KH2FM.NEW.ISO\"";

            if (!File.Exists("..\\Patcher\\KH2FM.NEW.ISO"))
                MessageBox.Show("KH2FM.NEW.ISO not found. Please make sure the launcher is located in the Music Tool folder.", "KH2FM.NEW.ISO Not Found");

            // Check if fullscreen/game window only is checked
            if (Fullscreen)
            {
                ps2.StartInfo.Arguments += " --fullscreen";
            }
            if (GameWindowOnly)
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
            music.StartInfo.FileName = ".\\Music_GUI.exe";
            try
            {
                music.Start();
            }
            catch
            {
                MessageBox.Show("Music GUI not found. Please make sure the launcher is located in the Music Tool folder.", "Music GUI Not Found");
            }

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

            // Start volume saver
            Thread volumeSaver = new(Volume_Saver);
            volumeSaver.Start();

            // Puts the launcher in the background to keep volume saver running
            Hide();

            // Closes launcher when Music_GUI closes to assure volume was saved
            music.WaitForExit();

            // Manually abort thread to workaround 0 volume bug
            // TODO: Find a better solution
#pragma warning disable SYSLIB0006
            volumeSaver.Abort();
#pragma warning restore SYSLIB0006

            Environment.Exit(0);
        }
        
        private void Volume_Saver()
        {
            data = parser.ReadFile("config.ini");

            while (!Found)
            {
                Thread.Sleep(250);
                Found = Memory.Attach("Music_GUI");
            }

            Process[] processes = Process.GetProcessesByName("Music_GUI");
            int BaseAddress = processes[0].MainModule.BaseAddress.ToInt32();

            // Get volume
            try
            {
                volume = int.Parse(data["Music GUI"]["Volume"]);
            }
            catch
            {
                // File doesn't exist yet, so get volume from memory
                volume = Memory.ReadMemory<int>(BaseAddress + 0x251010);
            }
            
            // Save volume
            Memory.WriteMemory<int>(BaseAddress + 0x251010, volume);
            data["Music GUI"]["Volume"] = volume.ToString();
            parser.WriteFile("config.ini", data);

            // If volume changed, save new volume
            bool volumeChanged = false;
            DateTime lastVolumeChange = DateTime.Now;
            while (true)
            {
                Thread.Sleep(100);

                // Exit loop if Music_GUI is closed, so memory does not read from invalid address
                if (Process.GetProcessesByName("Music_GUI").Length == 0)
                    break;

                new_volume = Memory.ReadMemory<int>(BaseAddress + 0x251010);
   
                if (new_volume != volume)
                {
                    volume = new_volume;
                    data["Music GUI"]["Volume"] = volume.ToString();
                    parser.WriteFile("config.ini", data);
                    volumeChanged = true;
                    lastVolumeChange = DateTime.Now;
                }
                else if (volumeChanged && (DateTime.Now - lastVolumeChange).TotalSeconds > 30)
                {
                    volumeChanged = false;
                    Thread.Sleep(1000); // wait for 1 second before checking again
                }
            }
        }

        private void SaveConfig(string section, string key, string value)
        {
            data = parser.ReadFile("config.ini");
            data[section][key] = value;
            parser.WriteFile("config.ini", data);
        }

        #region WPF Methods
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            
        }

        private void CheckBoxFS_Checked(object sender, RoutedEventArgs e)
        {
            Fullscreen = true;
            SaveConfig("Settings", "Fullscreen", "True");
        }

        private void CheckBoxFS_Unchecked(object sender, RoutedEventArgs e)
        {
            Fullscreen = false;
            SaveConfig("Settings", "Fullscreen", "False");
        }

        private void CheckBoxGW_Checked(object sender, RoutedEventArgs e)
        {
            GameWindowOnly = true;
            SaveConfig("Settings", "GameWindowOnly", "True");
        }

        private void CheckBoxGW_Unchecked(object sender, RoutedEventArgs e)
        {
            GameWindowOnly = false;
            SaveConfig("Settings", "GameWindowOnly", "False");
        }

        private void CheckBoxSkipLauncher_Checked(object sender, RoutedEventArgs e)
        {
            SkipLauncher = true;
            SaveConfig("Settings", "SkipLauncher", "True");
            MessageBox.Show("The next time NMC Launcher is opened, PCSX2 and Music GUI will launch immedately.\n\nNote: To turn this off, you must change SkipLauncher from True to False in the config.ini file found in the Music Tool folder.", "Warning");
        }

        private void CheckBoxSkipLauncher_Unchecked(object sender, RoutedEventArgs e)
        {
            SkipLauncher = false;
            SaveConfig("Settings", "SkipLauncher", "False");
        }
        #endregion

        // Credit to PuffingIn2D and C0reTheAlpaca for the Memory class
        internal class Memory
        {
            private static Process? m_iProcess;
            private static IntPtr m_iProcessHandle;

            private static int m_iBytesWritten;
            private static int m_iBytesRead;

            public static bool Attach(string ProcName)
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
