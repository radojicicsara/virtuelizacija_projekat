using Common;
using System;
using System.Globalization;
using System.IO;
using System.ServiceModel;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            string datasetRoot = @"C:\Users\sara\Documents\virtuelizacija\Dataset";

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
                        // Kreiramo novi proxy za svaki fajl
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
                            proxy.StartSession(meta);
                            SendSamples(proxy, csvFile);
                            proxy.EndSession();
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
                            factory.Close();
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
                // Preskačemo header red
                string headerLine = reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Preskačemo prazne redove
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(',');

                    // CSV ima 6 kolona: Frequency, R, X, V, T, Range
                    if (parts.Length < 6)
                    {
                        LogInvalidFile(csvFile, $"Red {rowIndex} ima manje od 6 kolona: {line}");
                        rowIndex++;
                        continue;
                    }

                    // Parsiramo sa InvariantCulture (tačka kao decimalni separator)
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

                    // Šaljemo samo 39 redova
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
            // Format: Hk_IFR14500_SoC_100_03-07-2023_11-46
            // Tražimo deo posle "SoC_"
            string marker = "SoC_";
            int idx = fileNameWithoutExtension.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;

            string afterMarker = fileNameWithoutExtension.Substring(idx + marker.Length);
            // Uzimamo sve do sledeće "_"
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