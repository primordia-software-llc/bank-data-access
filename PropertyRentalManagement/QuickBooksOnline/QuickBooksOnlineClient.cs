﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using AwsTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyRentalManagement.DatabaseModel;
using PropertyRentalManagement.DataServices;
using PropertyRentalManagement.QuickBooksOnline.Models;

namespace PropertyRentalManagement.QuickBooksOnline
{
    public class QuickBooksOnlineClient
    {
        public QuickBooksOnlineBearerToken Token { get; set; }
        public ILogging Logger { get; }
        public string RealmId { get; }

        public QuickBooksOnlineClient(string realmId, DatabaseClient<QuickBooksOnlineConnection> dbClient, ILogging logger)
        {
            RealmId = realmId;
            Logger = logger;
            var qboConnection = dbClient.Get(new QuickBooksOnlineConnection { RealmId = realmId });
            Token = OAuthClient.GetAccessToken(
                qboConnection.ClientId,
                qboConnection.ClientSecret,
                qboConnection.RefreshToken,
                logger);
            dbClient.Update(
                qboConnection.GetKey(),
                new QuickBooksOnlineConnection
                {
                    AccessToken = Token.AccessToken,
                    RefreshToken = Token.RefreshToken
                });
            // Token will get stale during process, more work needs to be done, but that is why the token isn't injected. Token has to be retreived in the client to do the refresh mid-process.
        }

        public int QueryCount<T>(string query) where T : IQuickBooksOnlineEntity, new()
        {
            var result = Request($"query?query={HttpUtility.UrlEncode(query)}", HttpMethod.Get);
            var json = JObject.Parse(result);
            return json["QueryResponse"]["totalCount"].Value<int>();
        }

        public List<T> QueryAll<T>(string query) where T : IQuickBooksOnlineEntity, new()
        {
            var count = QueryCount<T>(query.Replace("select * from", "select count(*)from"));
            var maxResults = 100;
            var allResults = new List<T>();
            for (int startPosition = 0; startPosition < count; startPosition += maxResults)
            {
                var pagedQuery = $"{query} STARTPOSITION {startPosition} MAXRESULTS {maxResults}";
                var result = Request($"query?query={HttpUtility.UrlEncode(pagedQuery)}", HttpMethod.Get);
                var json = JObject.Parse(result);
                var entityResults = json["QueryResponse"][new T().EntityName];
                allResults.AddRange(JsonConvert.DeserializeObject<IList<T>>(entityResults.ToString()));
            }

            return allResults;
        }

        public List<T> Query<T>(string query) where T : IQuickBooksOnlineEntity, new()
        {
            var result = Request($"query?query={HttpUtility.UrlEncode(query)}", HttpMethod.Get);
            var json = JObject.Parse(result);
            var entityResults = json["QueryResponse"][new T().EntityName];
            if (entityResults == null)
            {
                return new List<T>();
            }
            return JsonConvert.DeserializeObject<List<T>>(entityResults.ToString());
        }

        public T Create<T>(string path, T model) where T : IQuickBooksOnlineEntity, new()
        {
            var jsonInput = JsonConvert.SerializeObject(model, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var result = Request(path, HttpMethod.Post, jsonInput);
            var json = JObject.Parse(result);
            return JsonConvert.DeserializeObject<T>(json[new T().EntityName].ToString());
        }

        public string Request(string path, HttpMethod method, string body = null)
        {
            var client = new HttpClient { BaseAddress = new Uri("https://quickbooks.api.intuit.com") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token.AccessToken);

            var relativePath = $"/v3/company/{RealmId}/{path}";
            var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var result = client.SendAsync(request).Result;
            var response = result.Content.ReadAsStringAsync().Result;
            if (!result.IsSuccessStatusCode)
            {
                Logger.Log($"QuickBooks Online API Request Failure"
                           + $" {(int) result.StatusCode} GET {relativePath}"
                           + $" Received {response}");
            }
            result.EnsureSuccessStatusCode();

            return response;
        }
    }
}
