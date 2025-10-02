using MangaReader.WebUI.Models;
using MangaReader.WebUI.Models.ViewModels.Manga;
using MangaReader.WebUI.Services.APIServices.MangaReaderLibApiClients.Interfaces;
using MangaReader.WebUI.Services.MangaServices;
using MangaReader.WebUI.Services.MangaServices.DataProcessing.Interfaces.MangaReaderLibMappers;
using MangaReader.WebUI.Services.MangaServices.MangaPageService;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MangaReader.WebUI.Controllers
{
    public class MangaController : Controller
    {
        private readonly IMangaReaderLibTagClient _tagClient;
        private readonly IMangaReaderLibAuthorClient _authorClient;
        private readonly IMangaReaderLibToTagListResponseMapper _tagListResponseMapper;
        private readonly ILogger<MangaController> _logger;
        private readonly MangaDetailsService _mangaDetailsService;
        private readonly MangaSearchService _mangaSearchService;

        public MangaController(
            IMangaReaderLibTagClient tagClient,
            IMangaReaderLibAuthorClient authorClient,
            IMangaReaderLibToTagListResponseMapper tagListResponseMapper,
            ILogger<MangaController> logger,
            MangaDetailsService mangaDetailsService,
            MangaSearchService mangaSearchService)
        {
            _tagClient = tagClient;
            _authorClient = authorClient;
            _tagListResponseMapper = tagListResponseMapper;
            _logger = logger;
            _mangaDetailsService = mangaDetailsService;
            _mangaSearchService = mangaSearchService;
        }

        private static class SessionKeys
        {
            public const string CurrentSearchResultData = "CurrentSearchResultData";
        }

        [HttpGet]
        [Route("api/manga/tags")]
        public async Task<IActionResult> GetTags()
        {
            try
            {
                _logger.LogInformation("Đang lấy danh sách tags từ MangaReaderLib API");
                var tagsDataFromLib = await _tagClient.GetTagsAsync(limit: 500);
                if (tagsDataFromLib == null)
                {
                    throw new Exception("API không trả về dữ liệu tags.");
                }
                var tagsForFrontend = _tagListResponseMapper.MapToTagListResponse(tagsDataFromLib);
                return Json(new { success = true, data = tagsForFrontend });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách tags từ MangaReaderLib API.");
                return Json(new { success = false, error = "Không thể tải danh sách tags." });
            }
        }

        // GET: Manga/Details/5
        public async Task<IActionResult> Details(string id)
        {
            // Logic này đã có từ commit trước
            try
            {
                var viewModel = await _mangaDetailsService.GetMangaDetailsAsync(id);
                return View("Details", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải chi tiết manga: {Message}", ex.Message);
                ViewBag.ErrorMessage = "Không thể tải chi tiết manga. Vui lòng thử lại sau.";
                return View("Details", new MangaDetailViewModel());
            }
        }

        // GET: Manga/Search
        public async Task<IActionResult> Search(
            string title = "", List<string>? status = null, string sortBy = "updatedAt",
            string? authors = null, string? artists = null, int? year = null,
            List<string>? publicationDemographic = null, List<string>? contentRating = null,
            string includedTagsMode = "AND", string excludedTagsMode = "OR",
            string includedTagsStr = "", string excludedTagsStr = "",
            int page = 1, int pageSize = 24)
        {
            try
            {
                _logger.LogInformation("[SEARCH_VIEW] Bắt đầu action Search với page={Page}, pageSize={PageSize}", page, pageSize);
                var authorIds = new List<string>();
                if (!string.IsNullOrEmpty(authors)) authorIds.AddRange(authors.Split(',', StringSplitOptions.RemoveEmptyEntries));
                if (!string.IsNullOrEmpty(artists)) authorIds.AddRange(artists.Split(',', StringSplitOptions.RemoveEmptyEntries));
                var uniqueAuthorIds = authorIds.Distinct().ToList();

                var sortManga = _mangaSearchService.CreateSortMangaFromParameters(
                    title, status, sortBy, uniqueAuthorIds, year,
                    publicationDemographic, contentRating,
                    includedTagsMode, excludedTagsMode, includedTagsStr, excludedTagsStr);

                var viewModel = await _mangaSearchService.SearchMangaAsync(page, pageSize, sortManga);

                if (viewModel.Mangas != null && viewModel.Mangas.Any())
                {
                    HttpContext.Session.SetString(SessionKeys.CurrentSearchResultData, JsonSerializer.Serialize(viewModel.Mangas));
                }
                ViewData["InitialViewMode"] = Request.Cookies["MangaViewMode"] ?? "grid";
                return View("Search", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách manga.");
                ViewBag.ErrorMessage = $"Không thể tải danh sách manga. Chi tiết: {ex.Message}";
                return View("Search", new MangaListViewModel { SortOptions = new SortManga { Title = title, Status = status, SortBy = sortBy } });
            }
        }

        // ... các action helper cho search ...
        [HttpGet]
        public async Task<IActionResult> GetSearchResultsPartial( /* ... params ... */ ) { /* ... logic ... */ return PartialView("_SearchResultsWrapperPartial"); }

        [HttpGet]
        public IActionResult GetMangaViewPartial(string viewMode = "grid") { /* ... logic ... */ return PartialView("_SearchResultsPartial"); }

        [HttpGet("api/manga/search-authors")]
        public async Task<IActionResult> SearchAuthors([FromQuery] string nameFilter)
        {
            if (string.IsNullOrWhiteSpace(nameFilter) || nameFilter.Length < 2)
            {
                return Ok(new List<AuthorSearchResultViewModel>());
            }
            try
            {
                var authorResponse = await _authorClient.GetAuthorsAsync(nameFilter: nameFilter, limit: 10);
                if (authorResponse?.Data == null) return Ok(new List<AuthorSearchResultViewModel>());
                var results = authorResponse.Data.Select(a => new AuthorSearchResultViewModel
                {
                    Id = Guid.Parse(a.Id),
                    Name = a.Attributes.Name
                }).ToList();
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm tác giả: {NameFilter}", nameFilter);
                return StatusCode(500, "Lỗi máy chủ.");
            }
        }
    }
}