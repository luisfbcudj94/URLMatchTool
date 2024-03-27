using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using SeleniumUndetectedChromeDriver;
using CsvHelper;
using CsvHelper.Configuration;

namespace Tool
{
    internal class Program
    {
        public static ConcurrentBag<RedirectionOutputModel> results = [];

        // list to capture redirects
        private static List<(string Url, int StatusCode)> Redirects = [];
        private static Uri DestinationUri;

        private const int HttpOkStatus = 200;
        private const int timeoutSeconds = 15;
        static async Task Main(string[] args)
        {
            try
            {
                // Check if the URL file path is provided
                if (args.Length > 3 || args.Length < 1)
                {
                    Console.WriteLine("Usage: URLRedirectionValidator.exe Path_To_url_list.txt");
                    Console.WriteLine("Usage: URLRedirectionValidator.exe Path_To_url_list.txt 1 (Show browser)");
                    Console.WriteLine("Usage: URLRedirectionValidator.exe Path_To_url_list.txt 0 1 (Open Result in Excel)");
                    return;
                }

                Console.WriteLine("--------------------------\nUrl Redirection Validator\n--------------------------\n");

                int iteration = 1;
                var inputs = ReadInputFile(args[0]);

                // Check if there are at least two arguments and the second one is either "0" or "1".
                // If not, or if parsing fails, default to "0" (which is presumably the non-headless mode).
                var hideBrowser = true;

                if (args.Length > 1 && int.TryParse(args[1], out var headlessInt) && headlessInt == 1)
                {
                    hideBrowser = false;
                }
                //var csvFilePath = $@"{DateTime.Now:yyyy-MM-dd_hh_mm_ss}_result.csv";
                var csvFilePath = $@"_result.csv";
                Console.WriteLine($"Writing output to: {csvFilePath}\n");

                // call this function under parallel for each
                using (var driver = UndetectedChromeDriver.Create(driverExecutablePath: await new ChromeDriverInstaller().Auto(), headless: hideBrowser))
                {
                    foreach (var input in inputs)
                    {
                        // reset redirect url fro  previous run
                        Redirects = [];

                        // set Destination url
                        DestinationUri = new Uri(input.DestinationURL);

                        // log progress
                        Console.Write($"\n{iteration,4:0}. Testing redirection for: {DestinationUri.Host,-50}");

                        // set timeout
                        var timeout = DateTime.Now.AddSeconds(timeoutSeconds);

                        // call the function to test the url
                        await FetchUrlProcessor(input.RedirectionURL, iteration, driver, csvFilePath, timeout);

                        // inrement the iterator
                        iteration++;
                    }

                    // Close the browser
                    driver.Quit();
                }

                Console.WriteLine($"\n\nResults are saved to {csvFilePath}");

                // Check if there are at least three arguments and the third one is either "0" or "1".
                var openResultInExcel = false;

                if (args.Length > 2 && int.TryParse(args[2], out var openCsvResult) && openCsvResult == 1)
                {
                    openResultInExcel = true;
                }

                if (openResultInExcel)
                {
                    Console.WriteLine($"Opening results in Excel: {csvFilePath}");
                    try
                    {
                        Process.Start(new ProcessStartInfo(csvFilePath) { UseShellExecute = true });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to open CSV in Excel: {e.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nUnexpected error: {ex.Message}");
            }

            Console.WriteLine("Processing complete.");
        }
        static async Task FetchUrlProcessor(string inputUrl, int index, UndetectedChromeDriver driver, string filePath, DateTime timeout)
        {
            List<JToken> responseList = [];

            DevToolsSession session = ((IDevTools)driver).GetDevToolsSession();
            await session.Domains.Network.EnableNetwork();
            session.DevToolsEventReceived += OnDevToolsEventReceived;

            try
            {
                // Set the page load timeout
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(150);

                // Add a delay to ensure the page loads completely after redirection
                Thread.Sleep(1200);

                driver.Navigate().GoToUrl(inputUrl);

                // Add a delay to ensure the page loads completely after redirection
                Thread.Sleep(800);

                // Wait until the page is completely loaded
                WebDriverWait wait = new(driver, TimeSpan.FromSeconds(30));
                wait.Until(driver => (driver as IJavaScriptExecutor).ExecuteScript("return document.readyState").Equals("complete"));

                // Add a delay to ensure the page loads completely after redirection
                Thread.Sleep(800);

                // Output result
                var result = new RedirectionOutputModel()
                {
                    Index = index,
                    RedirectionURL = inputUrl,
                    DestinationURL = $"{DestinationUri}",
                    DestinationDomain = DestinationUri.Host,
                };

                // Record the initial URL
                Redirects.Add((driver.Url, 200));

                // Follow redirects until the final destination is reached
                while (true)
                {
                    // Get the latest redirected url
                    var (Url, StatusCode) = Redirects.LastOrDefault();
                    Uri redirectedUri = new(Url);

                    // Check if there is a redirect
                    if (redirectedUri.Host != DestinationUri.Host && StatusCode != HttpOkStatus)
                    {
                        // Add a delay to ensure the page loads completely after redirection
                        Thread.Sleep(1000);

                        // Wait until the page is completely loaded
                        wait.Until(driver => (driver as IJavaScriptExecutor).ExecuteScript("return document.readyState").Equals("complete"));

                        // Add a delay to ensure the page loads completely after redirection
                        Thread.Sleep(1000);

                        // Check if the timeout task has completed
                        if (DateTime.Now > timeout)
                        {
                            // Timeout reached, break the loop
                            Console.Write($"Skipped - Timeout reached 15 seconds.");

                            // set result values
                            result.FinalDomain = redirectedUri.Host;
                            result.Status = "Skipped Timeout";
                            WriteToCsv(index, filePath, result);

                            break;
                        }

                        // Skip the url if it redirection more than 20 url
                        if (Redirects.Count > 20)
                        {
                            Console.Write($"Skipped - Due to redirecting for long time.");

                            result.FinalDomain = redirectedUri.Host;
                            result.Status = "Skipped Multiple Redirects";
                            WriteToCsv(index, filePath, result);
                            break;
                        }

                        // Navigate to the next URL
                        driver.Navigate().GoToUrl(driver.Url);

                        // Add a delay to ensure the page loads completely after redirection
                        Thread.Sleep(1500);
                    }
                    else
                    {
                        // Check if the status code is OK (200) and domain match with destination url
                        var status = redirectedUri.Host == DestinationUri.Host && StatusCode == HttpOkStatus ? "Success" : "Failure";

                        // log status
                        Console.Write($"{status}");

                        // Join the Item1 values into a comma-separated string
                        string redirects = string.Join("|", Redirects.Select(e => $"{e.Url}|{e.StatusCode}"));

                        // set result values
                        result.FinalDomain = redirectedUri.Host;
                        result.Status = status;
                        result.StatusCode = $"{StatusCode}";
                        result.FinalUrl = $"{redirectedUri}";
                        result.Redirects = redirects;

                        WriteToCsv(index, filePath, result);

                        // No more redirects, break the loop
                        break;
                    }
                }
            }
            catch (WebDriverTimeoutException ex)
            {
                Console.WriteLine($"Timeout error occurred: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nAn error occurred: " + ex.Message);
            }

            void OnDevToolsEventReceived(object sender, DevToolsEventReceivedEventArgs e)
            {
                if (e.EventName == "responseReceivedExtraInfo")
                {
                    JToken responseHeaders = e.EventData["headers"];
                    JToken responseEventData = e.EventData;

                    // Assuming responseHeaders is a JObject
                    JObject headersObject = (JObject)responseHeaders;
                    JObject headersResponseEventData = (JObject)responseEventData;

                    // parse status code
                    _ = int.TryParse(headersResponseEventData["statusCode"]?.ToString(), out var statusCode);

                    // parse url
                    var redirectedURL = headersObject.Value<string>("location") ?? null;

                    if (redirectedURL != null)
                    {
                        Redirects.Add((redirectedURL, statusCode));

                        // Check if The input contains a valid HTTPS URI.
                        if (Uri.TryCreate(redirectedURL, UriKind.Absolute, out Uri uriResult)
                            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                        {
                            if (statusCode == HttpOkStatus && uriResult.Host == DestinationUri.Host)
                            {
                                // Unsubscribe from the event
                                session.DevToolsEventReceived -= OnDevToolsEventReceived;
                            }
                        }
                    }
                }
            }
        }

        private static void WriteToCsv(int index, string filePath, RedirectionOutputModel result)
        {
            // Write data into a csv file
            using var writer = new StreamWriter(filePath, true);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Ensure header is written only once
            if (index == 1)
            {
                csv.WriteHeader<RedirectionOutputModel>();
                csv.NextRecord();
            }

            csv.WriteRecord(result);
            csv.NextRecord(); // Write the next record on a new line
        }

        static List<RedirectionModel> ReadInputFile(string filePath)
        {
            if (!Path.Exists(filePath))
                throw new Exception($"File not found: {filePath}");

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false, // Set to true if your CSV has a header
                Delimiter = ",", // Specify the delimiter used in the file
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);
            return csv.GetRecords<RedirectionModel>().ToList();
        }
    }
}
