using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Timers;
using System.Configuration;
using System.Data.SQLite;


namespace DiskHealthService
{
    public partial class Service1 : ServiceBase
    {
        private Timer _timer;
        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            // SQLite veritabanını oluştur
            CreateDatabaseIfNotExists();
            CheckDiskHealth();

            EventLog.WriteEntry("DiskHealthService started.");

            _timer = new Timer(3600);
            _timer.Elapsed += TimerElapsed;
            _timer.Start();
        }
        private void CreateDatabaseIfNotExists()
        {
            try
            {
                string databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DiskHealth.db");
                string connectionString = $"Data Source={databasePath};Version=3;";


                if (!File.Exists("DiskHealth.db"))
                {
                    SQLiteConnection.CreateFile("DiskHealth.db");
                    Console.WriteLine("DiskHealth.db dosyası oluşturuluyor...");

                    using (var connection = new SQLiteConnection(connectionString))
                    {
                        connection.Open();
                        string createTableQuery = "CREATE TABLE IF NOT EXISTS DiskLogs (" +
                                                  "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                                                  "DriveName TEXT, " +
                                                  "UsagePercentage REAL, " +
                                                  "TotalSize INTEGER, " +
                                                  "FreeSpace INTEGER, " +
                                                  "LogTime DATETIME, " +
                                                  "Message TEXT)";
                        using (var command = new SQLiteCommand(createTableQuery, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }

                    Console.WriteLine("DiskHealth.db ve tablo oluşturuldu.");
                }
                else
                {
                    Console.WriteLine("DiskHealth.db zaten mevcut.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Veritabanı oluşturma sırasında hata: {ex.Message}");
            }
        }


        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            CheckDiskHealth();
            CheckSmartStatus();
        }

        private void CheckDiskHealth()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        string driveName = drive.Name;
                        long totalSize = drive.TotalSize;
                        long freeSpace = drive.TotalFreeSpace;

                        double usagePercentage = ((double)(totalSize - freeSpace) / totalSize) * 100;

                        string message = $"Sürücü: {driveName}, Kullanım: %{usagePercentage:F2}, " +
                                         $"Toplam: {FormatBytes(totalSize)}, Boş: {FormatBytes(freeSpace)}";

                        // Loglama
                        EventLog.WriteEntry(message, EventLogEntryType.Information);
                        LogToFile(message);
                        LogToDatabase(driveName, usagePercentage, totalSize, freeSpace, message);

                        if (usagePercentage > 90)
                        {
                            string warningMessage = $"UYARI! {driveName} sürücüsü %90'dan fazla dolu.";
                            EventLog.WriteEntry(warningMessage, EventLogEntryType.Warning);
                            LogToFile(warningMessage);
                            LogToDatabase(driveName, usagePercentage, totalSize, freeSpace, warningMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Disk kontrolünde hata: {ex.Message}";
                EventLog.WriteEntry(errorMessage, EventLogEntryType.Error);
                LogToFile(errorMessage);
            }
        }


        private string FormatBytes(long bytes)
        {
            // Byte'ları okunabilir formata çevirme (KB, MB, GB)
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:F2} {sizes[order]}";
        }

        private void CheckSmartStatus()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        string model = disk["Model"]?.ToString();
                        string status = disk["Status"]?.ToString();

                        string message = $"Disk Modeli: {model}, Durum: {status}";
                        EventLog.WriteEntry(message, EventLogEntryType.Information);
                        LogToFile(message);

                        if (status != "OK")
                        {
                            string warningMessage = $"UYARI! Disk ({model}) durumu kritik: {status}";
                            EventLog.WriteEntry(warningMessage, EventLogEntryType.Warning);
                            LogToFile(warningMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"SMART durumu kontrolünde hata: {ex.Message}";
                EventLog.WriteEntry(errorMessage, EventLogEntryType.Error);
                LogToFile(errorMessage);
            }
        }


        private void SendEmailAlert(string subject, string body)
        {
            try
            {
                string smtpServer = ConfigurationManager.AppSettings["SmtpServer"];
                int smtpPort = int.Parse(ConfigurationManager.AppSettings["SmtpPort"]);
                string emailFrom = ConfigurationManager.AppSettings["EmailFrom"];
                string emailPassword = ConfigurationManager.AppSettings["EmailPassword"];
                string emailTo = ConfigurationManager.AppSettings["EmailTo"];

                var smtpClient = new SmtpClient(smtpServer)
                {
                    Port = smtpPort,
                    Credentials = new NetworkCredential(emailFrom, emailPassword),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(emailFrom),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false,
                };

                mailMessage.To.Add(emailTo);

                smtpClient.Send(mailMessage);

                LogToFile("Kritik durum e-postası gönderildi.");
            }
            catch (Exception ex)
            {
                LogToFile($"E-posta gönderimi sırasında hata: {ex.Message}");
            }
        } 
        private void LogToFile(string message)
        {
            try
            {
                string logDirectory = AppDomain.CurrentDomain.BaseDirectory + "\\Logs";
                string logFile = Path.Combine(logDirectory, "DiskHealthServiceLog.txt");

                // Log klasörünü oluştur
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // Mesajı log dosyasına yaz
                using (StreamWriter writer = new StreamWriter(logFile, true))
                {
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"Log dosyasına yazılamadı: {ex.Message}", EventLogEntryType.Error);
            }
        }
        private void LogToDatabase(string driveName, double usagePercentage, long totalSize, long freeSpace, string message)
        {
            string connectionString = "Data Source=DiskHealth.db;Version=3;";

            // SQLite bağlantısı ve komut
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string query = "INSERT INTO DiskLogs (DriveName, UsagePercentage, TotalSize, FreeSpace, LogTime, Message) " +
                               "VALUES (@DriveName, @UsagePercentage, @TotalSize, @FreeSpace, @LogTime, @Message)";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@DriveName", driveName);
                    command.Parameters.AddWithValue("@UsagePercentage", usagePercentage);
                    command.Parameters.AddWithValue("@TotalSize", totalSize);
                    command.Parameters.AddWithValue("@FreeSpace", freeSpace);
                    command.Parameters.AddWithValue("@LogTime", DateTime.Now);
                    command.Parameters.AddWithValue("@Message", message);

                    command.ExecuteNonQuery();
                }
            }
        }
        protected override void OnStop()
        {
            _timer.Stop();

            EventLog.WriteEntry("DiskHealthService has stopped.");
        }
    }
}
