using MarketingCodingAssignment.Models;
using MarketingCodingAssignment.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MarketingCodingAssignment.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private SearchEngine _searchEngine;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
            _searchEngine = new SearchEngine();
        }

        public IActionResult Index()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public JsonResult Search(String searchString, int start, int rows)
        {
            var searchResults = _searchEngine.Search(searchString, start, rows);

            return Json(new {searchResults, numFound = searchResults.Count() });

        }

        [HttpPost]
        public void ReloadIndex()
        {
            DeleteIndex();
            PopulateIndex();
            return;
        }

        // Delete the contents of the lucene index 
        public void DeleteIndex()
        {
            _searchEngine.DeleteIndex();
            return;
        }

        // Read the data from the csv and feed it into the lucene index
        public void PopulateIndex()
        {
            // Get the list of films from the csv file
            var csvFilms = _searchEngine.ReadFilmsFromCsv();

            // Convert to Lucene format
            List<FilmLuceneRecord> luceneFilms = csvFilms.Select(x => new FilmLuceneRecord
            {
                Id = x.Id,
                Title = x.Title,
                Overview = x.Overview,
                Runtime = int.TryParse(x.Runtime, out int parsed) ? parsed : 0,
                Tagline = x.Tagline
            }).ToList();

            // Write the records to the lucene index
            _searchEngine.PopulateIndex(luceneFilms);

            return;
        }




    }
}

