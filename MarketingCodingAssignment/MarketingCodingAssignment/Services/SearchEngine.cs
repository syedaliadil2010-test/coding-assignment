﻿using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using MarketingCodingAssignment.Models;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Lucene.Net.Analysis.En;

namespace MarketingCodingAssignment.Services
{
    public class SearchEngine
    {
        // The code below is roughly based on sample code from: https://lucenenet.apache.org/

        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
        private static readonly SearchEngine _searchEngine;
        static SearchEngine()
        {
            // Code to Populate Indexes for First time
            _searchEngine = new SearchEngine();
            _searchEngine.DeleteIndex();
            _searchEngine.PopulateIndexFromCsv();
        }

        public SearchEngine()
        {
            
        }

        public List<FilmCsvRecord> ReadFilmsFromCsv()
        {
            List<FilmCsvRecord> records = new();
            string filePath = $"{System.IO.Directory.GetCurrentDirectory()}{@"\wwwroot\csv"}" + "\\" + "FilmsInfo.csv";
            using (StreamReader reader = new(filePath))
            using (CsvReader csv = new(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                records = csv.GetRecords<FilmCsvRecord>().ToList();

            }
            using (StreamReader r = new(filePath))
            {
                string csvFileText = r.ReadToEnd();
            }
            return records;
        }

        // Read the data from the csv and feed it into the lucene index
        public void PopulateIndexFromCsv()
        {
            // Get the list of films from the csv file
            var csvFilms = ReadFilmsFromCsv();

            // Convert to Lucene format
            List<FilmLuceneRecord> luceneFilms = csvFilms.Select(x => new FilmLuceneRecord
            {
                Id = x.Id,
                Title = x.Title,
                Overview = x.Overview,
                Runtime = int.TryParse(x.Runtime, out int parsedRuntime) ? parsedRuntime : 0,
                Tagline = x.Tagline,
                Revenue = long.TryParse(x.Revenue, out long parsedRevenue) ? parsedRevenue : 0,
                VoteAverage = double.TryParse(x.VoteAverage, out double parsedVoteAverage) ? parsedVoteAverage : 0,
                ReleaseDate = DateTime.TryParse(x.ReleaseDate, out DateTime parsedReleasedDate) ? parsedReleasedDate : Convert.ToDateTime("1900-01-01")
            }).ToList();

            // Write the records to the lucene index
            PopulateIndex(luceneFilms);

            return;
        }

        public void PopulateIndex(List<FilmLuceneRecord> films)
        {
            // Construct a machine-independent path for the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);

            // Create an analyzer to process the text
            StandardAnalyzer analyzer = new(AppLuceneVersion);

            // Create an index writer
            IndexWriterConfig indexConfig = new(AppLuceneVersion, analyzer);
            using IndexWriter writer = new(dir, indexConfig);
           
                //Add to the index
                foreach (var film in films)
                {
                    Document doc = new()
                {
                    new StringField("Id", film.Id, Field.Store.YES),
                    new TextField("Title", film.Title, Field.Store.YES),
                    new TextField("Overview", film.Overview, Field.Store.YES),
                    new Int32Field("Runtime", film.Runtime, Field.Store.YES),
                    new TextField("Tagline", film.Tagline, Field.Store.YES),
                    new Int64Field("Revenue", film.Revenue ?? 0, Field.Store.YES),
                    new DoubleField("VoteAverage", film.VoteAverage ?? 0.0, Field.Store.YES),
                    new StringField("ReleaseDate", film.ReleaseDate.ToString(), Field.Store.YES),
                    new TextField("CombinedText", film.Title + " " + film.Tagline + " " + film.Overview, Field.Store.NO)
                };
                    writer.AddDocument(doc);
                }

                writer.Flush(triggerMerge: false, applyAllDeletes: false);
                writer.Commit();
                writer.Dispose();
           
           return;
        }

        public void DeleteIndex()
        {
            // Delete everything from the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);
            StandardAnalyzer analyzer = new(AppLuceneVersion);
            IndexWriterConfig indexConfig = new(AppLuceneVersion, analyzer);
            using IndexWriter writer = new(dir, indexConfig); 
                writer.DeleteAll();
                writer.Commit();
            return;
        }

        public SearchResultsViewModel Search(string searchString, int startPage, int rowsPerPage, int? durationMinimum, int? durationMaximum, double? voteAverageMinimum, DateTime? minDate, DateTime? maxDate)
        {
            // Construct a machine-independent path for the index
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string indexPath = Path.Combine(basePath, "index");
            using FSDirectory dir = FSDirectory.Open(indexPath);
            using DirectoryReader reader = DirectoryReader.Open(dir);
            IndexSearcher searcher = new(reader);

            int hitsLimit = 1000;
            TopScoreDocCollector collector = TopScoreDocCollector.Create(hitsLimit, true);

            var query = this.GetLuceneQuery(searchString, durationMinimum, durationMaximum, voteAverageMinimum, minDate, maxDate);

            searcher.Search(query, collector);

            int startIndex = (startPage) * rowsPerPage;
            TopDocs hits = collector.GetTopDocs(startIndex, rowsPerPage);
            ScoreDoc[] scoreDocs = hits.ScoreDocs;

            List<FilmLuceneRecord> films = new();
            foreach (ScoreDoc? hit in scoreDocs)
            {
                Document foundDoc = searcher.Doc(hit.Doc);
                FilmLuceneRecord film = new()
                {
                    Id = foundDoc.Get("Id").ToString(),
                    Title = foundDoc.Get("Title").ToString(),
                    Overview = foundDoc.Get("Overview").ToString(),
                    Runtime = int.TryParse(foundDoc.Get("Runtime"), out int parsedRuntime) ? parsedRuntime : 0,
                    Tagline = foundDoc.Get("Tagline").ToString(),
                    Revenue = long.TryParse(foundDoc.Get("Revenue"), out long parsedRevenue) ? parsedRevenue : 0,
                    VoteAverage =  double.TryParse(foundDoc.Get("VoteAverage"), out double parsedVoteAverage) ? parsedVoteAverage : 0.0,
                    ReleaseDate = DateTime.TryParse(foundDoc.Get("ReleaseDate"), out DateTime parsedReleasedDate) ? parsedReleasedDate : Convert.ToDateTime("1900-01-01"),
                    Score = hit.Score
                };
                films.Add(film);
            }

            SearchResultsViewModel searchResults = new()
            {
                RecordsCount = hits.TotalHits,
                Films = films.ToList()
            };

            return searchResults;
        }
        private Query GetLuceneQuery(string searchString, int? durationMinimum, int? durationMaximum, double? voteAverageMinimum, DateTime? minDate, DateTime? maxDate)
        {
            if (string.IsNullOrWhiteSpace(searchString))
            {
                // If there's no search string, just return everything.
                return new MatchAllDocsQuery();
            }

            var pq = new MultiPhraseQuery();
            foreach (var word in searchString.Split(" ").Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (!EnglishAnalyzer.DefaultStopSet.Contains(word))
                {
                    pq.Add(new Term("CombinedText", word.ToLowerInvariant()));
                }
            }

            Query rq = NumericRangeQuery.NewInt32Range("Runtime", durationMinimum, durationMaximum, true, true);
            // Setting Min vote Avg for filter
            Query vaq = NumericRangeQuery.NewDoubleRange("VoteAverage", voteAverageMinimum, 10.0, true, true);

            Query dateRangeQuery = TermRangeQuery.NewStringRange("ReleaseDate", minDate.ToString(), maxDate.ToString(), true, true);


            // Apply the filters.
            BooleanQuery bq = new()
            {
                { pq, Occur.MUST },
                { rq, Occur.MUST },
                { vaq, Occur.MUST },
                {dateRangeQuery, Occur.MUST }
            };

            return bq;
        }
    }
}

