using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RevitToRDFConverter
{


    public class HttpClientHelper
    {
        private static HttpClient client = new HttpClient();

        public static async Task<string> POSTDataAsync(string data)
        {

            //var data1 = data.ToString();
            //var data2 = new StringContent(JsonConvert.SerializeObject(data1), Encoding.UTF8, "text/turtle");

            //LHAM: Update "clear default" (Tømmer DB for triples). - Virker ikke...
            //var updateString = new StringContent("clear default", null, "text/turtle");
            //var urlUpdate = "http://localhost:3030/HTR-test1/data"; //LHAM: Har prøvet /query, /update, /data.
            //HttpResponseMessage responseUpdate = await client.PostAsync(urlUpdate, updateString);

            var data2 = new StringContent(data, null, "text/turtle");
            //var urlData = "http://localhost:3030/Ultra-Simple/data";
            var urlData = "http://localhost:3030/HTR-test1-KL/data";
            HttpResponseMessage response = await client.PostAsync(urlData, data2);

            string result = response.Content.ReadAsStringAsync().Result;
            return result;
        }
    }


}

