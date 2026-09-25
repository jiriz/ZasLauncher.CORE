using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.IO;
using System.Text.Json;
using ZASutility.Standard;

namespace ZasLauncherGUI.Utility;

public class Utils
{
    public static async Task<List<string>> GetXmlFromISK(string url)
    {
        List<string> result = new List<string>();
        try
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/xml")
            );

            request.Headers.TryAddWithoutValidation(
                "hwid",
                GetDeviceId()
            );
            request.Headers.TryAddWithoutValidation(
                "Connection",
                "Keep-Alive"
            );
            request.Content = new StringContent("");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync();
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xml);
            foreach (XmlNode item in xmlDocument.DocumentElement!
                         .SelectNodes("items/item")!)
            {
                var line = item
                    ?.SelectSingleNode("nazev")
                    ?.InnerText;

                if (!String.IsNullOrEmpty(line))
                {
                    if (!String.IsNullOrEmpty(item?.SelectSingleNode("cesta_exe")?.InnerText) ||
                        (bool)item?.SelectSingleNode("parametry")?.InnerText.Contains("#RDP_ID") ||
                        (bool)item?.SelectSingleNode("parametry")?.InnerText.Contains("#CREATE_ISK_PATCH;"))
                    {
                        line += "|" + item?.SelectSingleNode("cesta_exe")?.InnerText;
                        if (!String.IsNullOrEmpty(item?.SelectSingleNode("parametry")?.InnerText))
                        {
                            line += "|" + item?.SelectSingleNode("parametry")?.InnerText;
                        }
                    }
                }

                result.Add(line);
            }
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard(
                    "Chyba komunikace",
                    "Nepodařilo se načíst data ze serveru.\n\n" + ex.Message,
                    ButtonEnum.Ok,
                    Icon.Error);
            await box.ShowAsync();
        }

        return result;
    }

    public static string GetDeviceId()
    {
        var source =
            Environment.MachineName +
            "|" +
            Environment.UserName;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(
            Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash)[..32];
    }
    
    private static string GetHexString(byte[] bt)
    {
        string s = string.Empty;
        for (int i = 0; i < bt.Length; i++)
        {
            byte b = bt[i];
            int n, n1, n2;
            n = (int)b;
            n1 = n & 15;
            n2 = (n >> 4) & 15;
            if (n2 > 9)
                s += ((char)(n2 - 10 + (int)'A')).ToString();
            else
                s += n2.ToString();
            if (n1 > 9)
                s += ((char)(n1 - 10 + (int)'A')).ToString();
            else
                s += n1.ToString();
            if ((i + 1) != bt.Length && (i + 1) % 2 == 0) s += "-";
        }

        return s;
    }

    public static Task KillProcessesAsync(string processName)
    {
        return Task.Run(() =>
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                    // proces už mohl skončit / nejsou práva
                }
            }
        });
    }

    public static async Task KillProcessInAllWindowsVMs(string processName)
    {
        var listPsi = new ProcessStartInfo
        {
            FileName = ParallelsPrlctl.ExecutablePath,
            Arguments = "list --json",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var listProcess = Process.Start(listPsi);

        if (listProcess == null)
            return;

        var json = await listProcess.StandardOutput.ReadToEndAsync();

        await listProcess.WaitForExitAsync();

        using var doc = JsonDocument.Parse(json);

        foreach (var vm in doc.RootElement.EnumerateArray())
        {
            var name = vm.GetProperty("name").GetString();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            try
            {
                var killPsi = new ProcessStartInfo
                {
                    FileName = ParallelsPrlctl.ExecutablePath,
                    Arguments =
                        $"exec \"{name}\" taskkill /IM {processName}.exe /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var killProcess = Process.Start(killPsi);

                if (killProcess != null)
                    await killProcess.WaitForExitAsync();
            }
            catch
            {
                // ignore
            }
        }
    }

    public static async Task<string> GetRdpParamsAsync(string rdpId)
    {
        try
        {
            var url = "https://iserver.zasgroup.cz/zas-service/get-data-rdp" +
                      "?&token=BB0489CE-4C13-4E1E-897B-DEC4F706E5E4" +
                      "&user=" + Uri.EscapeDataString(Environment.UserName) +
                      "&rdp_id=" + Uri.EscapeDataString(rdpId);

            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("text/xml")
            );

            request.Headers.TryAddWithoutValidation(
                "hwid",
                GetDeviceId()
            );
            request.Headers.TryAddWithoutValidation(
                "Connection",
                "Keep-Alive"
            );
            request.Content = new StringContent("");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync();
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xml);
            xmlDocument.LoadXml(xml);

            return MyUtility.GetStringXmlValue(xmlDocument.DocumentElement, "parametry");
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }
}
