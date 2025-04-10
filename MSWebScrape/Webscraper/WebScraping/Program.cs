//nuget package manager console commands: [Should automatically find/install these if you use the VisualStudio solution+project]
//  Install - Package HtmlAgilityPack
//  Install - Package Selenium.WebDriver
//  Install - Package Selenium.WebDriver.ChromeDriver

using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools.V132.Debugger;
using OpenQA.Selenium.Interactions;

namespace WebScraping
{
    internal class Program
    {
        /// <summary>
        /// Get readable text using web driver
        /// </summary>
        /// <param name="driver">web driver, eg Selenium</param>
        /// <param name="url">url to download from</param>
        /// <returns>The readable part of the web page</returns>
        static async Task<string> GetReadableText(IWebDriver driver, string url)
        { 
            driver.Navigate().GoToUrl(url);

            // A simple wait - in production, use explicit waits **Not sure why/if this is needed, but it's probably to give Javascript etc. time to run
            await Task.Delay(2000);

            // Extract visible text from the rendered page
            return driver.FindElement(By.TagName("body")).Text;
        }

        static async Task Main(string[] args)
        {
            string rootURL = "https://web.makespace.org/";                          //starting url
            string site = "makespace.org";                                          //only pages within the site are crawled
            List<string> ignoreContains = new() {"mailto:","special:","?share=","action=","printable=","file:","?continue","/signin/","/uploads/"}; //ignore page links containing this
            ignoreContains.Add("user:");                                            //don't crawl users
            ignoreContains.Add("wiki.makespace");                                   //don't crawl the old wiki as it is superceded by the new equipment pages
            List<string> ignoreEndsWith = new() { ".jpg","png",".zip",".pdf"};      //ignore these file endings
            bool GetAllURLsFromFile = File.Exists("AllURLs.txt");                   //grab from file if it exists


            List<string> urlsCrawled = new();

            if (GetAllURLsFromFile)
            {
                Console.WriteLine("Loading list of URLs");
                urlsCrawled = File.ReadAllLines("AllURLs.txt").ToList();
            }
            else
            { 
                Console.WriteLine($"Reading pages starting from {rootURL}");

                List<string> urls = new();
                urls.Add(rootURL);

                while (urls.Count > 0)
                {
                    var url = urls[0];
                    urls.RemoveAt(0);

                    bool ignore = false;

                    if (urlsCrawled.Contains(url))
                        continue;                           //already crawled

                    if (!url.Contains(site))
                        ignore = true;                      //not a matching site

                    foreach (var ignoreString in ignoreContains)
                    {
                        if (ignore)
                            break;
                        if (url.Contains(ignoreString,StringComparison.OrdinalIgnoreCase))
                            ignore = true;
                    }

                    foreach (var ignoreEndsWithString in ignoreEndsWith)
                    {
                        if (ignore)
                            break;
                        if (url.EndsWith(ignoreEndsWithString, StringComparison.OrdinalIgnoreCase))
                            ignore = true;
                    }

                    if (ignore)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Ignoring URL: {url}");
                        Console.ResetColor();
                        continue;
                    }

                    Console.WriteLine($"Adding URL: {url}");

                    urlsCrawled.Add(url);                                   // mark the new page done

                    var urlsFromPage = await GetURLs(url);
                    urls.AddRange(urlsFromPage);

                    await Task.Delay(250);                                  // Don't spam the server too hard
                }
                Console.WriteLine("Saving list of URLs");
                File.WriteAllLines("AllURLs.txt", urlsCrawled);
            }

            //Download the readable text from each page and save it

            var options = new ChromeOptions();
            options.AddArgument("headless");                            // Configure Chrome for headless mode
            using IWebDriver driver = new ChromeDriver(options);

            foreach (var url in urlsCrawled)
            {
                try
                {
                    Console.WriteLine($"Downloading URL: {url}");
                    string text= await GetReadableText(driver, url);
                    string URLPage = new Uri(url).AbsolutePath.TrimStart('/');
                    if (URLPage == "")
                        URLPage = "root";
                    URLPage = URLPage.Replace("/","-");                     // Make page name suitable for a filename
                    URLPage = URLPage.Replace("?","-");
                    URLPage = URLPage.Replace("&", "-");
                    if (URLPage=="")
                        continue;
                    using StreamWriter file = File.CreateText(@"MS\" + URLPage);
                    file.Write(text);
                    file.Close();
                    await Task.Delay(250);                                  // Don't spam the server too hard
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  !!Download failed or saving failed: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// Return a list of absolute urls
        /// </summary>
        /// <param name="url">url to get</param>
        /// <returns>list of absolute urls</returns>
        static async Task<List<string>> GetURLs(string url)
        {
            var result = new List<string>();
            HttpClient client = new HttpClient();

            //Download the page
            string html;
            try
            {
                html = await client.GetStringAsync(url);
            }
            catch (Exception ex)
            { 
                Console.WriteLine("Page get failed: " + ex.Message);        //eg 404 page not found
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract the links
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links == null)
                return result;                                              //eg no links found

            var baseURI = new Uri(url);
            foreach (var link in links)
            {
                //Get the actual link part from the href
                var href = link.Attributes["href"].Value;

                //convert relative url to absolute
                var absoluteURI = new Uri(baseURI, href);

                //remove page fragment etc
                var cleanAbsoluteUrl = absoluteURI.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);

                if (cleanAbsoluteUrl.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(cleanAbsoluteUrl);
            }

            return result;
        }
    }
}
