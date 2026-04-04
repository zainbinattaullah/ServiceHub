using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Areas.HR.Models
{
    public class Store
    {
        [Key]
       public int Id { get; set; }
       public string StoreCode { get; set; }
       public string StoreName { get; set; }
       public string Area { get; set; }
       public string Region { get; set; }
       public string Department { get; set; }
    }
}
