using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Mmu.U4DataMaintenance.Functions.Services
{
    public interface IUnit4Service
    {
        Task<JObject> SendAsync(string path, string query, string payload, bool update);
    }
}
