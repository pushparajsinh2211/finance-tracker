using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public CategoriesController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private Postgrest.Client GetPostgrestClient()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                throw new UnauthorizedAccessException("Missing Authorization header.");

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var options = new Postgrest.ClientOptions 
            { 
                Headers = new Dictionary<string, string> 
                { 
                    { "Authorization", $"Bearer {token}" },
                    { "apikey", _configuration["Supabase:AnonKey"]! }
                } 
            };
            return new Postgrest.Client($"{_configuration["Supabase:Url"]}/rest/v1", options);
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var response = await GetPostgrestClient().Table<Category>().Get();
                var dtos = response.Models.Select(c => new CategoryDto
                {
                    Id = c.Id,
                    FamilyId = c.FamilyId,
                    Name = c.Name,
                    Color = c.Color ?? "#9e9e9e",
                    IsDefault = c.IsDefault
                }).OrderBy(c => c.Name).ToList();
                
                return Ok(dtos);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
        {
            try
            {
                var newCat = new Category
                {
                    FamilyId = request.FamilyId,
                    Name = request.Name,
                    Color = request.Color ?? "#9e9e9e",
                    IsDefault = false,
                    IsArchived = false
                };
                var response = await GetPostgrestClient().Table<Category>().Insert(newCat);
                var created = response.Models.FirstOrDefault();
                
                if (created == null) return BadRequest();

                return Ok(new CategoryDto
                {
                    Id = created.Id,
                    FamilyId = created.FamilyId,
                    Name = created.Name,
                    Color = created.Color ?? "#9e9e9e",
                    IsDefault = created.IsDefault
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var catResponse = await postgrest.Table<Category>().Where(x => x.Id == id).Get();
                var cat = catResponse.Models.FirstOrDefault();

                if (cat == null) return NotFound(new { Message = "Category not found." });

                if (request.Name != null) cat.Name = request.Name;
                if (request.Color != null) cat.Color = request.Color;
                if (request.IsArchived.HasValue) cat.IsArchived = request.IsArchived.Value;

                await postgrest.Table<Category>().Update(cat);
                
                return Ok(new CategoryDto
                {
                    Id = cat.Id,
                    FamilyId = cat.FamilyId,
                    Name = cat.Name,
                    Color = cat.Color ?? "#9e9e9e",
                    IsDefault = cat.IsDefault
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateCategoryRequest
    {
        public Guid FamilyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
    }

    public class UpdateCategoryRequest
    {
        public string? Name { get; set; }
        public string? Color { get; set; }
        public bool? IsArchived { get; set; }
    }
}
