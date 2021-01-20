﻿using System;
using System.Collections.Generic;
using System.Linq;
using AwsDataAccess;
using Newtonsoft.Json;
using NodaTime;
using NodaTime.Extensions;
using PropertyRentalManagement.DatabaseModel;
using PropertyRentalManagement.QuickBooksOnline;
using PropertyRentalManagement.QuickBooksOnline.Models;
using PropertyRentalManagement.QuickBooksOnline.Models.Invoices;
using PropertyRentalManagement.QuickBooksOnline.Models.Payments;
using Vendor = PropertyRentalManagement.DatabaseModel.Vendor;

namespace PropertyRentalManagement.BusinessLogic
{
    public class ReceiptSave
    {
        private DatabaseClient<ReceiptSaveResult> ReceiptDbClient { get; }
        private DatabaseClient<SpotReservation> SpotReservationDbClient { get; }
        private QuickBooksOnlineClient QuickBooksClient { get; }
        private decimal TaxRate { get; }

        public ReceiptSave(
            DatabaseClient<ReceiptSaveResult> receiptDbClient,
            QuickBooksOnlineClient quickBooksClient,
            decimal taxRate,
            DatabaseClient<SpotReservation> spotReservationDbClient)
        {
            ReceiptDbClient = receiptDbClient;
            QuickBooksClient = quickBooksClient;
            TaxRate = taxRate;
            SpotReservationDbClient = spotReservationDbClient;
        }

        public ReceiptSaveResult SaveReceipt(
            Receipt receipt,
            string customerId,
            string firstName,
            string lastName,
            string email,
            Vendor vendor)
        {
            ReceiptSaveResult result = new ReceiptSaveResult
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Receipt = JsonConvert.DeserializeObject<Receipt>(JsonConvert.SerializeObject(receipt)),
                Vendor = vendor,
                CreatedBy = new ReceiptSaveResultUser
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email
                }
            };
            ReceiptDbClient.Create(result);

            var memo = receipt.Memo;
            if (receipt.Spots != null && receipt.Spots.Any())
            {
                memo += Environment.NewLine;
                memo += "Spots: " + string.Join(", ", receipt.Spots.Select(x => $"{x.Section?.Name} - {x.Name}"));
            }

            if (receipt.RentalAmount > 0)
            {
                result.Invoice = QuickBooksClient.Create(CreateInvoice(customerId, receipt.RentalAmount, receipt.RentalDate, memo));
            }

            result.Payments = new List<Payment>();
            if (receipt.ThisPayment > 0)
            {
                ZonedClock easternClock = SystemClock.Instance.InZone(DateTimeZoneProviders.Tzdb["America/New_York"]);
                var paymentMemo = $"True Payment Date: {easternClock.GetCurrentDate():yyyy-MM-dd}{Environment.NewLine}{memo}";

                var unpaidInvoices = QuickBooksClient.QueryAll<Invoice>($"select * from Invoice where Balance != '0' and CustomerRef = '{customerId}' ORDERBY TxnDate");
                decimal payment = receipt.ThisPayment.GetValueOrDefault();
                var paymentApplicator = new PaymentApplicator(QuickBooksClient);
                foreach (var unpaidInvoice in unpaidInvoices)
                {
                    var paymentAppliedToInvoice = paymentApplicator.CreatePayment(
                        unpaidInvoice,
                        customerId,
                        payment,
                        receipt.RentalDate,
                        paymentMemo);
                    result.Payments.Add(paymentAppliedToInvoice);
                    payment -= paymentAppliedToInvoice.TotalAmount.GetValueOrDefault();
                    if (payment <= 0)
                    {
                        break;
                    }
                }
                if (payment > 0)
                {
                    var unappliedPayment = paymentApplicator.CreatePayment(
                        null,
                        customerId,
                        payment,
                        receipt.RentalDate,
                        paymentMemo
                    );
                    result.Payments.Add(unappliedPayment);
                }
            }

            ReceiptDbClient.Create(result);

            if (receipt.Spots != null)
            {
                foreach (var spot in receipt.Spots)
                {
                    SpotReservationDbClient.Create(new SpotReservation
                    {
                        SpotId = spot.Id,
                        RentalDate = receipt.RentalDate,
                        QuickBooksOnlineId = int.Parse(customerId),
                        VendorId = vendor.Id
                    });
                }
            }

            return result;
        }

        private Invoice CreateInvoice(string customerId, decimal? rentalAmount, string transactionDate, string memo)
        {
            decimal quantity = 1;
            decimal taxableAmount = rentalAmount.GetValueOrDefault() / (1 + TaxRate);
            var invoice = new Invoice
            {
                TxnDate = transactionDate,
                CustomerRef = new QuickBooksOnline.Models.Reference { Value = customerId },
                Line = new List<SalesLine>
                {
                    new SalesLine
                    {
                        DetailType = "SalesItemLineDetail",
                        SalesItemLineDetail = new SalesItemLineDetail
                        {
                            ItemRef = new QuickBooksOnline.Models.Reference { Value = Constants.QUICKBOOKS_PRODUCT_RENT.ToString() },
                            Quantity = quantity,
                            TaxCodeRef = new QuickBooksOnline.Models.Reference { Value = Constants.QUICKBOOKS_INVOICE_LINE_TAXABLE },
                            UnitPrice = taxableAmount
                        },
                        Amount = quantity * taxableAmount
                    }
                },
                TxnTaxDetail = new TxnTaxDetail
                {
                    TxnTaxCodeRef = new QuickBooksOnline.Models.Reference { Value = Constants.QUICKBOOKS_RENTAL_TAX_RATE.ToString() }
                },
                PrivateNote = memo,
                SalesTermRef = new QuickBooksOnline.Models.Reference { Value = Constants.QUICKBOOKS_TERMS_DUE_NOW.ToString() }
            };
            return invoice;
        }
    }
}
