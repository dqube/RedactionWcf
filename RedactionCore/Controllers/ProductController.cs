using Microsoft.AspNetCore.Mvc;

namespace RedactionCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private static List<Product> _products = new List<Product>();
        private static int _nextId = 1;
        private readonly ILogger<ProductController> _logger;

        public ProductController(ILogger<ProductController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Get all products
        /// </summary>
        [HttpGet(Name = "GetAllProducts")]
        public ActionResult<IEnumerable<Product>> GetAll()
        {
            return Ok(_products);
        }

        /// <summary>
        /// Get product by ID
        /// </summary>
        [HttpGet("{id}", Name = "GetProductById")]
        public ActionResult<Product> GetById(int id)
        {
            var product = _products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return NotFound(new { message = $"Product with ID {id} not found" });
            }
            return Ok(product);
        }

        /// <summary>
        /// Create a new product
        /// </summary>
        [HttpPost(Name = "CreateProduct")]
        public ActionResult<Product> Create([FromBody] Product product)
        {
            if (product == null)
            {
                return BadRequest(new { message = "Product data is required" });
            }

            if (string.IsNullOrWhiteSpace(product.Name))
            {
                return BadRequest(new { message = "Product name is required" });
            }

            product.Id = _nextId++;
            _products.Add(product);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        /// <summary>
        /// Update an existing product
        /// </summary>
        [HttpPut("{id}", Name = "UpdateProduct")]
        public ActionResult Update(int id, [FromBody] Product product)
        {
            if (product == null)
            {
                return BadRequest(new { message = "Product data is required" });
            }

            var existingProduct = _products.FirstOrDefault(p => p.Id == id);
            if (existingProduct == null)
            {
                return NotFound(new { message = $"Product with ID {id} not found" });
            }

            existingProduct.Name = product.Name;
            existingProduct.Description = product.Description;
            existingProduct.Price = product.Price;
            existingProduct.Category = product.Category;
            existingProduct.StockQuantity = product.StockQuantity;

            return NoContent();
        }

        /// <summary>
        /// Delete a product
        /// </summary>
        [HttpDelete("{id}", Name = "DeleteProduct")]
        public ActionResult Delete(int id)
        {
            var product = _products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return NotFound(new { message = $"Product with ID {id} not found" });
            }

            _products.Remove(product);
            return NoContent();
        }
    }
}

namespace RedactionCore
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
    }
}
