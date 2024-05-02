using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Remoting.Contexts;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLauncher
{
    public partial class frmReplayLauncher : Form
    {
        public frmReplayLauncher()
        {
            InitializeComponent();
            AllowDrop = true;
            DragEnter += new DragEventHandler(frmReplayLauncher_DragEnter);
            DragDrop += new DragEventHandler(frmReplayLauncher_DragDrop);
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        }

        void frmReplayLauncher_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void frmReplayLauncher_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if(files.Length != 1)
            {
                MessageBox.Show("Please, only 1 file");
                return;
            }

            var vgkRunning = IsServiceRunning("vgk"); //Vanguard Kernel
            var vgcRunning = IsServiceRunning("vgc"); //Vanguard Usermode Client

            if (!vgkRunning)
            {
                //TODO: We can just launch the replay... but where the fk is the exe
                MessageBox.Show("Vanguard is turned off, you can just launch the replay like you did previously");
                return;
            }

            if(!vgcRunning)
            {
                MessageBox.Show("League Client not running, please launch it");
                return;
            }

            //Verify it is indeed a replay
            string file = files[0];
            bool validReplay = false;
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    //Tons of what should be redundant checks because content creators are amazing at breaking things
                    if (fs.Length > 4)
                    {
                        string magic = new string(br.ReadChars(4));
                        validReplay = magic == "RIOT";
                    }
                }
            }
            catch
            {
                MessageBox.Show("Error: Unable to open the Replay File to check its valid");
                return;
            }

            if(!validReplay)
            {
                MessageBox.Show("Something is up with the replay");
                return;
            }

            //This is where we need to get spicey with vanguard
            //Firstly we need league client api access
            var processes = Process.GetProcessesByName("LeagueClientUx");
            if (processes.Length == 0) 
            {
                MessageBox.Show("League Client isn't Open");
                return;
            }
            var leagueClient = processes[0];
            //Get location so we can get the lockfile info
            string exe = leagueClient.MainModule.FileName;
            string baseFolder = Path.GetDirectoryName(exe);
            string apiPort = "";
            string apiPwd = "";
            try
            {
                using (var fs = new FileStream($"{baseFolder}\\lockfile", FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string[] api = sr.ReadToEnd().Split(':');
                    apiPort = api[2];
                    apiPwd = api[3];
                }
            }
            catch
            {
                MessageBox.Show("Error: To read LeagueClient lockfile");
                return;
            }

            if(string.IsNullOrWhiteSpace(apiPort) || string.IsNullOrWhiteSpace(apiPwd)) 
            {
                MessageBox.Show("Failed to get League Client API access");
                return;
            }

            string apiUrlBase = $"https://127.0.0.1:{apiPort}";
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Base64Encode($"riot:{apiPwd}"));
            //We need to get replay folder location from it
            var rsp = httpClient.GetAsync($"{apiUrlBase}/lol-replays/v1/rofls/path").Result;
            var ReplayPath = rsp.Content.ReadAsStringAsync().Result.Replace("\"", "");
            //Copy the replay into it
            if (File.Exists($"{ReplayPath}/TEMPREPLAY-1.rofl"))
            {
                try { File.Delete($"{ReplayPath}/TEMPREPLAY-1.rofl"); }
                catch { MessageBox.Show("Failed to delete old temp replay"); return; }
            }

            try
            {                
                File.Copy(file, $"{ReplayPath}/TEMPREPLAY-1.rofl");
            }
            catch
            {
                MessageBox.Show("Failed to Copy the replay to the replay folder");
                return;
            }
            //Run the scan api            
            rsp = httpClient.PostAsync($"{apiUrlBase}/lol-replays/v1/rofls/scan", null).Result;
            //then we can launch it, client splits on the '-' to parse the number in the filename
            //so since its called TEMPREPLAY-1 the gameid we launch via the api is 1
            var empty = new StringContent("{}", Encoding.UTF8, "application/json");
            rsp = httpClient.PostAsync($"{apiUrlBase}/lol-replays/v1/rofls/1/watch", empty).Result;
            //OR we inject into a riot process to launch it but I don't wanna
            //Clean up because memory leak is bad apparently
            leagueClient.Dispose();
            httpClient.Dispose();
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public bool IsServiceRunning(string service)
        {
            ServiceController sc = new ServiceController(service);
            bool running = sc.Status == ServiceControllerStatus.Running;
            sc.Dispose();
            return running;
        }
    }
}

