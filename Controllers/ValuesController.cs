using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace RedactionWcf.Controllers
{
    public class ValuesController : ApiController
    {
        // GET api/values
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/values/5
        public string Get(int id)
        {
            return "value";
        }

        // POST api/values
        [HttpPost]
        public IHttpActionResult Post([FromBody] SampleData data)
        {
            if (data == null)
            {
                return BadRequest("Sample data cannot be null");
            }

            // Process the data here
            // For example, save to database

            return Ok(data);
        }

        // PUT api/values/5
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        public void Delete(int id)
        {
        }

    }
    public class SampleData
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
