﻿using System;
using PropertyRentalManagement.DataServices;
using System.Linq;
using PropertyRentalManagement.BusinessLogic;
using PropertyRentalManagement.QuickBooksOnline.Models;
using Xunit;
using Xunit.Abstractions;

namespace FinanceApi.Tests.InfrastructureAsCode.PointOfSale
{
    public class CreateVendorsFromQuickBooksOnlineAndIdentifyPaymentFrequency
    {
        private ITestOutputHelper Output { get; }

        public CreateVendorsFromQuickBooksOnlineAndIdentifyPaymentFrequency(ITestOutputHelper output)
        {
            Output = output;
        }

        //[Fact]
        public void Run()
        {
            var qboClient = Factory .CreateQuickBooksOnlineClient(new XUnitLogger(Output));
            var awsDbClient = Factory.CreateAmazonDynamoDbClient();

            var activeCustomers = qboClient.QueryAll<Customer>("select * from Customer Where Active = true");
            var saleReportService = new SaleReportService();
            foreach (var customer in activeCustomers)
            {
                var marchStart = new DateTime(2020, 3, 1);
                var marchEnd = new DateTime(2020, 3, 31);
                var marchSales = saleReportService.GetSales(qboClient, customer.Id,
                    marchStart, marchEnd);
                var oneReceiptMarch = marchSales.Invoices.Count + marchSales.SalesReceipts.Count == 1;
                var maySales = saleReportService.GetSales(qboClient, customer.Id,
                    new DateTime(2020, 5, 1),
                    new DateTime(2020, 5, 31));
                var oneReceiptMay = maySales.Invoices.Count + maySales.SalesReceipts.Count == 1;
                var juneStart = new DateTime(2020, 6, 1);
                var juneEnd = new DateTime(2020, 6, 30);
                var juneSales = saleReportService.GetSales(qboClient, customer.Id,
                    juneStart,
                    juneEnd);
                var oneReceiptJune = juneSales.Invoices.Count + juneSales.SalesReceipts.Count == 1;

                string paymentFrequency = oneReceiptMarch && oneReceiptMay && oneReceiptJune ? "monthly" : string.Empty;

                decimal? rentPrice = null;

                if (paymentFrequency == "monthly")
                {
                    if (marchSales.SalesReceipts.SingleOrDefault() != null)
                    {
                        rentPrice = marchSales.SalesReceipts.Single().TotalAmount;
                    }
                    if (marchSales.Invoices.SingleOrDefault() != null)
                    {
                        rentPrice = marchSales.Invoices.Single().TotalAmount;
                    }
                }

                new VendorService().Create(awsDbClient, int.Parse(customer.Id), paymentFrequency, rentPrice, string.Empty);
            }
        }

    }
}
