﻿using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using FinanceApi.DatabaseModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyRentalManagement.DatabaseModel;
using PropertyRentalManagement.DataServices;
using PropertyRentalManagement.QuickBooksOnline;
using PropertyRentalManagement.QuickBooksOnline.Models;

namespace FinanceApi.Routes.Authenticated.PointOfSale
{
    public class GetCustomers : IRoute
    {
        public string HttpMethod => "GET";
        public string Path => "/point-of-sale/customers";
        public void Run(APIGatewayProxyRequest request, APIGatewayProxyResponse response, FinanceUser user)
        {
            if (!new PointOfSaleAuthorization().IsAuthorized(user.Email))
            {
                response.StatusCode = 400;
                response.Body = new JObject {{"error", "unknown email"}}.ToString();
                return;
            }
            var databaseClient = new DatabaseClient<QuickBooksOnlineConnection>(new AmazonDynamoDBClient());
            var qboClient = new QuickBooksOnlineClient(Configuration.RealmId, databaseClient, new Logger());
            var customers = qboClient.QueryAll<Customer>("select * from customer");
            response.Body = JsonConvert.SerializeObject(customers);
        }
    }
}