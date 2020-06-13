﻿using Newtonsoft.Json;

namespace PropertyRentalManagement.QuickBooksOnline.Models.Invoices
{
    public class InvoiceLine
    {
        [JsonProperty("DetailType")]
        public string DetailType { get; set; }

        [JsonProperty("SalesItemLineDetail")]
        public SalesItemLineDetail SalesItemLineDetail { get; set; }

        [JsonProperty("Amount")]
        public decimal Amount { get; set; }
    }
}
