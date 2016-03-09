﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Web;
using System.Diagnostics;

namespace SendGrid.CSharp.HTTP.Client
{
    public class Response
    {
        public HttpStatusCode StatusCode;
        public HttpContent ResponseBody;
        public HttpResponseHeaders ResponseHeaders;

        public Response(HttpStatusCode statusCode, HttpContent responseBody, HttpResponseHeaders responseHeaders)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            ResponseHeaders = responseHeaders;
        }

        public virtual Dictionary<string, dynamic> DeserializeResponseBody(HttpContent content)
        {
            JavaScriptSerializer jss = new JavaScriptSerializer();
            var dsContent = jss.Deserialize<Dictionary<string, dynamic>>(content.ReadAsStringAsync().Result);
            return dsContent;
        }

        public virtual Dictionary<string, string> DeserializeResponseHeaders(HttpResponseHeaders content)
        {
            var dsContent = new Dictionary<string, string>();
            foreach (var pair in content )
            {
                dsContent.Add(pair.Key, pair.Value.First());
            }
            return dsContent;
        }

    }

    public class Client : DynamicObject
    {
        public string Host;
        public Dictionary <string,string> RequestHeaders;
        public string Version;
        public string UrlPath;
        public string MediaType;
        public enum Methods
        {
            DELETE, GET, PATCH, POST, PUT
        }

        public Client(string host, Dictionary<string,string> requestHeaders = null, string version = null, string urlPath = null)
        {
            Host = host;
            if(requestHeaders != null)
            {
                RequestHeaders = (RequestHeaders != null)
                    ? RequestHeaders.Union(requestHeaders).ToDictionary(pair => pair.Key, pair => pair.Value) : requestHeaders;
            }
            Version = (version != null) ? version : null;
            UrlPath = (urlPath != null) ? urlPath : null;
        }

        private string BuildUrl(string query_params = null)
        {
            string endpoint = null;
            if( Version != null)
            {
                endpoint = Host + "/" + Version + UrlPath;
            }
            else
            {
                endpoint = Host + UrlPath;
            }

            if (query_params != null)
            {
                JavaScriptSerializer jss = new JavaScriptSerializer();
                var ds_query_params = jss.Deserialize<Dictionary<string, dynamic>>(query_params);
                var query = HttpUtility.ParseQueryString(string.Empty);
                foreach (var pair in ds_query_params)
                {
                    query[pair.Key] = pair.Value.ToString();
                }
                string queryString = query.ToString();
                endpoint = endpoint + "?" + queryString;
            }
            
            return endpoint;
        }

        private Client BuildClient(string name = null)
        {
            string endpoint;
            if (name != null)
            {
                endpoint = UrlPath + "/" + name;
            }
            else
            {
                endpoint = UrlPath;
            }
            UrlPath = null; // Reset the current object's state before we return a new one
            return new Client(Host, RequestHeaders, Version, endpoint);
        }

        public virtual AuthenticationHeaderValue AddAuthorization(KeyValuePair<string, string> header)
        {
            string[] split = header.Value.Split(new char[0]);
            return new AuthenticationHeaderValue(split[0], split[1]);
        }

        public virtual void AddVersion(string version)
        {
            Version = version;
        }

        // Magic method to handle special cases
        public Client _(string magic)
        {
            return BuildClient(magic);
        }

        // Reflection
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = BuildClient(binder.Name);
            return true;
        }

        // Catch final method call
        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            if (binder.Name == "version")
            {
                AddVersion(args[0].ToString());
                result = BuildClient();
                return true;
            }

            string query_params = null;
            string request_body = null;
            int i = 0;
            foreach (object obj in args)
            {
                string name = binder.CallInfo.ArgumentNames.Count > i ?
                   binder.CallInfo.ArgumentNames[i] : null;
                if(name == "query_params")
                {
                    query_params = obj.ToString();
                }
                else if (name == "request_body")
                {
                    request_body = obj.ToString();
                }
                i++;
            }

            if( Enum.IsDefined(typeof(Methods), binder.Name.ToUpper()))
            {
                result = RequestAsync(binder.Name.ToUpper(), request_body: request_body, query_params: query_params).Result;
                return true;
            }
            else
            {
                result = null;
                return false;
            }

        }

        public async virtual Task<Response> MakeRequest(HttpClient client, HttpRequestMessage request)
        {
            HttpResponseMessage response = await client.SendAsync(request);
            return new Response(response.StatusCode, response.Content, response.Headers);
        }

        private async Task<Response> RequestAsync(string method, String request_body = null, String query_params = null)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    client.BaseAddress = new Uri(Host);
                    string endpoint = BuildUrl(query_params);
                    client.DefaultRequestHeaders.Accept.Clear();
                    if(RequestHeaders != null)
                    {
                        foreach (KeyValuePair<string, string> header in RequestHeaders)
                        {
                            if (header.Key == "Authorization")
                            {
                                client.DefaultRequestHeaders.Authorization = AddAuthorization(header);
                            }
                            else if (header.Key == "Content-Type")
                            {
                                MediaType = header.Value;
                                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
                            }
                            else
                            {
                                client.DefaultRequestHeaders.Add(header.Key, header.Value);
                            }
                        }
                    }

                    StringContent content = null;
                    if (request_body != null)
                    {
                        content = new StringContent(request_body.ToString().Replace("'", "\""), Encoding.UTF8, MediaType);
                    }

                    HttpRequestMessage request = new HttpRequestMessage
                    {
                        Method = new HttpMethod(method),
                        RequestUri = new Uri(endpoint),
                        Content = content
                    };
                    return await MakeRequest(client, request);

                }
                catch (Exception ex)
                {
                    HttpResponseMessage response = new HttpResponseMessage();
                    string message;
                    message = (ex is HttpRequestException) ? ".NET HttpRequestException" : ".NET Exception";
                    message = message + ", raw message: \n\n";
                    response.Content = new StringContent(message + ex.Message);
                    return new Response(response.StatusCode, response.Content, response.Headers);
                }
            }
        }
    }
}
