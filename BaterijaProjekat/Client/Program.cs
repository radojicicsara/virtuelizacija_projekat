using Common;
using System;
using System.Globalization;
using System.IO;
using System.ServiceModel;
using System.Configuration;
using System.Threading;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            string datasetRoot = ConfigurationManager.AppSettings["DatasetPath"];

            System.Threading.Thread.Sleep(1000);

            foreach (string batteryFolder in Directory.GetDirectories(datasetRoot))
            {
                string batteryId = Path.GetFileName(batteryFolder);

                string eisFolder = Path.Combine(batteryFolder, "EIS measurements");
                if (!Directory.Exists(eisFolder)) continue;

                foreach (string testFolder in Directory.GetDirectories(eisFolder))
                {
                    string testId = Path.GetFileName(testFolder);

                    string hiokiFolder = Path.Combine(testFolder, "Hioki");
                    if (!Directory.Exists(hiokiFolder)) continue;

                    foreach (string csvFile in Directory.GetFiles(hiokiFolder, "*.csv"))
                    {
                        ChannelFactory<IBatteryService> factory = new ChannelFactory<IBatteryService>("BatteryService");
                        IBatteryService proxy = factory.CreateChannel();

                        string fileName = Path.GetFileNameWithoutExtension(csvFile);
                        int soc = ParseSoC(fileName);
                        if (soc < 0)
                        {
                            LogInvalidFile(csvFile, "Ne može se parsirati SoC iz naziva fajla.");
                            continue;
                        }

                        EisMeta meta = new EisMeta
                        {
                            BatteryId = batteryId,
                            TestId = testId,
                            SoC = soc,
                            FileName = Path.GetFileName(csvFile),
                            TotalRows = 39
                        };

                        try
                        {
                            string ack1 = proxy.StartSession(meta);
                            Console.WriteLine("[ACK] Sesija zapoceta.");
                            SendSamples(proxy, csvFile);
                            string ack2 = proxy.EndSession();
                            Console.WriteLine("[ACK] Sesija zavrsena.");
                            Console.WriteLine($"[KLIJENT] Završeno: {batteryId} {testId} SoC={soc}%");
                        }
                        catch (FaultException<DataFormatFault> e)
                        {
                            Console.WriteLine($"[GREŠKA FORMAT] {e.Detail.Message}");
                        }
                        catch (FaultException<ValidationFault> e)
                        {
                            Console.WriteLine($"[GREŠKA VALIDACIJA] {e.Detail.Message}");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"[GREŠKA] {e.Message}");
                        }
                        finally
                        {
                            try { factory.Close(); }
                            catch { factory.Abort(); }
                        }
                    }
                }
            }

            Console.WriteLine("Prenos završen. Pritisnite Enter.");
            Console.ReadLine();
        }

        static void SendSamples(IBatteryService proxy, string csvFile)
        {
            int rowIndex = 0;
            int sentRows = 0;

            using (StreamReader reader = new StreamReader(csvFile))
            {
                string headerLine = reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(',');

                    if (parts.Length < 6)
                    {
                        LogInvalidFile(csvFile, $"Red {rowIndex} ima manje od 6 kolona: {line}");
                        rowIndex++;
                        continue;
                    }

                    double freq = 0, r = 0, x = 0, t = 0, range = 0;
                    bool ok =
                        double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out freq) &
                        double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out r) &
                        double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out x) &
                        double.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out t) &
                        double.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out range);

                    if (!ok)
                    {
                        LogInvalidFile(csvFile, $"Red {rowIndex} ima nevalidne vrednosti: {line}");
                        rowIndex++;
                        continue;
                    }

                    if (sentRows >= 39) break;

                    EisSample sample = new EisSample
                    {
                        RowIndex = rowIndex,
                        FrequencyHz = freq,
                        R_ohm = r,
                        X_ohm = x,
                        T_degC = t,
                        Range_ohm = range,
                        TimestampLocal = DateTime.Now
                    };

                    proxy.PushSample(sample);
                    sentRows++;
                    rowIndex++;
                }
            }
        }

        static int ParseSoC(string fileNameWithoutExtension)
        {
            string marker = "SoC_";
            int idx = fileNameWithoutExtension.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;

            string afterMarker = fileNameWithoutExtension.Substring(idx + marker.Length);
            string socStr = afterMarker.Split('_')[0];

            if (int.TryParse(socStr, out int soc))
                return soc;

            return -1;
        }

        static void LogInvalidFile(string csvFile, string reason)
        {
            string logPath = "invalid_rows.log";
            string entry = $"{DateTime.Now} | Fajl: {Path.GetFileName(csvFile)} | Razlog: {reason}";
            File.AppendAllText(logPath, entry + Environment.NewLine);
            Console.WriteLine($"[LOG] {entry}");
        }
    }
}