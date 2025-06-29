using Core.Entities;
using Core.Interfaces;
using Core.DTOs;
using System.Globalization;

namespace Core.Services
{
    public class DrillHoleService : IDrillHoleService
    {
        private readonly IDrillHoleRepository _repository;

        public DrillHoleService(IDrillHoleRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<DrillHole>> GetAllDrillHolesAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<DrillHole?> GetDrillHoleByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID cannot be null or empty", nameof(id));

            return await _repository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<DrillHole>> GetDrillHolesByProjectIdAsync(int projectId)
        {
            return await _repository.GetByProjectIdAsync(projectId);
        }

        public async Task<IEnumerable<DrillHole>> GetDrillHolesBySiteIdAsync(int projectId, int siteId)
        {
            return await _repository.GetBySiteIdAsync(projectId, siteId);
        }

        public async Task<DrillHole> CreateDrillHoleAsync(DrillHole drillHole)
        {
            if (drillHole == null)
                throw new ArgumentNullException(nameof(drillHole));

            if (string.IsNullOrWhiteSpace(drillHole.Id))
                drillHole.Id = Guid.NewGuid().ToString();

            if (await _repository.ExistsAsync(drillHole.Id))
                throw new InvalidOperationException($"DrillHole with ID '{drillHole.Id}' already exists");

            ValidateDrillHole(drillHole);

            drillHole.CreatedAt = DateTime.UtcNow;
            drillHole.UpdatedAt = DateTime.UtcNow;

            return await _repository.AddAsync(drillHole);
        }

        public async Task UpdateDrillHoleAsync(DrillHole drillHole)
        {
            if (drillHole == null)
                throw new ArgumentNullException(nameof(drillHole));

            var existingDrillHole = await _repository.GetByIdAsync(drillHole.Id);
            if (existingDrillHole == null)
                throw new InvalidOperationException($"DrillHole with ID '{drillHole.Id}' not found");

            ValidateDrillHole(drillHole);

            drillHole.UpdatedAt = DateTime.UtcNow;
            drillHole.CreatedAt = existingDrillHole.CreatedAt; // Preserve original creation date

            await _repository.UpdateAsync(drillHole);
        }

        public async Task DeleteDrillHoleAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID cannot be null or empty", nameof(id));

            var existingDrillHole = await _repository.GetByIdAsync(id);
            if (existingDrillHole == null)
                throw new InvalidOperationException($"DrillHole with ID '{id}' not found");

            await _repository.DeleteAsync(id);
        }

        public async Task<IEnumerable<DrillHole>> CreateDrillHolesFromCsvAsync(CsvUploadRequest csvRequest)
        {
            if (csvRequest?.FileStream == null || csvRequest.FileSize == 0)
                throw new ArgumentException("File cannot be null or empty");

            // Clear existing drill holes before adding new ones from CSV
            await _repository.ClearAllAsync();

            var drillHoles = new List<DrillHole>();

            using var reader = new StreamReader(csvRequest.FileStream);
            
            // Skip the summary lines at the top (Number of Holes, Total drilling, etc.)
            string? line;
            var headers = new string[0];
            var dataStarted = false;
            var lineNumber = 0;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = line.Split(',').Select(v => v.Trim()).ToArray();
                
                // Look for the header line (starts with "Sr No.")
                if (!dataStarted && values.Length > 0 && values[0].Equals("Sr No.", StringComparison.OrdinalIgnoreCase))
                {
                    headers = values.Select(h => h.Trim().ToLowerInvariant()).ToArray();
                    dataStarted = true;
                    
                    // Validate that we have the required columns for blast data
                    var requiredHeaders = new List<string> 
                    { 
                        "id", 
                        "east", 
                        "north", 
                        "elev", 
                        "length", 
                        "azi", 
                        "dip", 
                        "actual dep", 
                        "stemming" 
                    };
                    var missingHeaders = new List<string>();
                    
                    foreach (var required in requiredHeaders)
                    {
                        bool found = false;
                        switch (required)
                        {
                            case "sr no.":
                                found = headers.Any(h => h == "sr no." || h == "sr no" || h == "serial no." || h == "serial number");
                                break;
                            case "id":
                                found = headers.Any(h => h == "id" || h == "hole id" || h == "holeid");
                                break;
                            case "east":
                                found = headers.Any(h => h == "east" || h == "easting" || h == "x");
                                break;
                            case "north":
                                found = headers.Any(h => h == "north" || h == "northing" || h == "y");
                                break;
                            case "elev":
                                found = headers.Any(h => h == "elev" || h == "elevation" || h == "z");
                                break;
                            case "length":
                                found = headers.Any(h => h == "length" || h == "hole length");
                                break;
                            case "azi":
                                found = headers.Any(h => h == "azi" || h == "azimuth" || h == "bearing");
                                break;
                            case "dip":
                                found = headers.Any(h => h == "dip" || h == "inclination" || h == "angle");
                                break;
                            case "actual dep":
                                found = headers.Any(h => h == "actual dep" || h == "actualdep" || h == "actual depth" || h == "actual_depth");
                                break;
                            case "stemming":
                                found = headers.Any(h => h == "stemming" || h == "stem");
                                break;
                            default:
                                found = headers.Contains(required);
                                break;
                        }
                        
                        if (!found)
                            missingHeaders.Add(required);
                    }
                    
                    if (missingHeaders.Any())
                        throw new InvalidOperationException($"Missing required CSV headers: {string.Join(", ", missingHeaders)}");
                    
                    continue;
                }
                
                // Skip lines before we find the header
                if (!dataStarted)
                    continue;
                
                // Skip empty lines or summary rows
                if (values.Length < headers.Length || string.IsNullOrWhiteSpace(values[1])) // Check if ID column is empty
                    continue;

                try
                {
                    var drillHole = ParseBlastDrillHoleFromCsvLine(headers, values, lineNumber);
                    
                    if (drillHole != null)
                    {
                        drillHoles.Add(drillHole);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error parsing line {lineNumber}: {ex.Message}");
                }
            }

            if (drillHoles.Any())
            {
                await _repository.AddRangeAsync(drillHoles);
            }

            return drillHoles;
        }

        private static DrillHole ParseBlastDrillHoleFromCsvLine(string[] headers, string[] values, int lineNumber)
        {
            var drillHole = new DrillHole
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            for (int i = 0; i < Math.Min(headers.Length, values.Length); i++)
            {
                var header = headers[i];
                var value = values[i];

                switch (header)
                {
                    case "sr no.":
                    case "sr no":
                    case "serial no.":
                    case "serial number":
                        break;
                    case "id":
                    case "hole id":
                    case "holeid":
                        drillHole.Id = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString() : value;
                        drillHole.Name = drillHole.Id; // Use ID as name for blast holes
                        break;
                    case "east":
                    case "easting":
                    case "x":
                        drillHole.Easting = ParseDouble(value, "Easting", lineNumber);
                        break;
                    case "north":
                    case "northing":
                    case "y":
                        drillHole.Northing = ParseDouble(value, "Northing", lineNumber);
                        break;
                    case "elev":
                    case "elevation":
                    case "z":
                        drillHole.Elevation = ParseDouble(value, "Elevation", lineNumber);
                        break;
                    case "length":
                    case "hole length":
                        drillHole.Length = ParseDouble(value, "Length", lineNumber);
                        drillHole.Depth = drillHole.Length; // Set Depth same as Length for compatibility
                        break;
                    case "azi":
                    case "azimuth":
                    case "bearing":
                        drillHole.Azimuth = ParseDouble(value, "Azimuth", lineNumber);
                        break;
                    case "dip":
                    case "inclination":
                    case "angle":
                        drillHole.Dip = ParseDouble(value, "Dip", lineNumber);
                        break;
                    case "actual dep":
                    case "actualdep":
                    case "actual depth":
                    case "actual_depth":
                        drillHole.ActualDepth = ParseDouble(value, "Actual Depth", lineNumber);
                        break;
                    case "stemming":
                    case "stem":
                        drillHole.Stemming = ParseDouble(value, "Stemming", lineNumber);
                        break;
                    // Explicitly ignore explosive-related fields
                    case "total charg":
                    case "emulsion":
                    case "anfo":
                    case "total explosive mass":
                        // Ignore these fields as requested
                        break;
                    default:
                        // Ignore any other unknown fields
                        break;
                }
            }

            ValidateDrillHole(drillHole);
            return drillHole;
        }

        // Keep the original method for backward compatibility with standard CSV format
        private static DrillHole ParseDrillHoleFromCsvLine(string[] headers, string[] values, int lineNumber)
        {
            var drillHole = new DrillHole
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                var value = values[i];

                switch (header)
                {
                    case "id":
                        drillHole.Id = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString() : value;
                        break;
                    case "name":
                        drillHole.Name = value ?? string.Empty;
                        break;
                    case "easting":
                        drillHole.Easting = ParseDouble(value, "Easting", lineNumber);
                        break;
                    case "northing":
                        drillHole.Northing = ParseDouble(value, "Northing", lineNumber);
                        break;
                    case "elevation":
                        drillHole.Elevation = ParseDouble(value, "Elevation", lineNumber);
                        break;
                    case "depth":
                        drillHole.Depth = ParseDouble(value, "Depth", lineNumber);
                        break;
                    case "azimuth":
                        drillHole.Azimuth = ParseDouble(value, "Azimuth", lineNumber);
                        break;
                    case "dip":
                        drillHole.Dip = ParseDouble(value, "Dip", lineNumber);
                        break;
                }
            }

            ValidateDrillHole(drillHole);
            return drillHole;
        }

        private static double ParseDouble(string value, string fieldName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0.0; // Default to 0 for empty values in blast data

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new InvalidOperationException($"Line {lineNumber}: Invalid {fieldName} value '{value}'");

            return result;
        }

        private static int ParseInt(string value, string fieldName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0; // Default to 0 for empty values in blast data

            if (!int.TryParse(value, out int result))
                throw new InvalidOperationException($"Line {lineNumber}: Invalid {fieldName} value '{value}'");

            return result;
        }

        private static void ValidateDrillHole(DrillHole drillHole)
        {
            if (string.IsNullOrWhiteSpace(drillHole.Id))
                throw new ArgumentException("DrillHole ID cannot be null or empty");

            if (string.IsNullOrWhiteSpace(drillHole.Name))
                drillHole.Name = drillHole.Id; // Use ID as name if name is empty

            if (drillHole.Depth < 0)
                throw new ArgumentException("DrillHole Depth cannot be negative");

            if (drillHole.Length < 0)
                throw new ArgumentException("DrillHole Length cannot be negative");

            if (drillHole.ActualDepth < 0)
                throw new ArgumentException("DrillHole Actual Depth cannot be negative");

            if (drillHole.Stemming < 0)
                throw new ArgumentException("DrillHole Stemming cannot be negative");

            // More lenient validation for blast hole data
            if (drillHole.Azimuth < 0 || drillHole.Azimuth > 360)
                drillHole.Azimuth = Math.Max(0, Math.Min(360, drillHole.Azimuth)); // Clamp to valid range

            if (drillHole.Dip < -90 || drillHole.Dip > 90)
                drillHole.Dip = Math.Max(-90, Math.Min(90, drillHole.Dip)); // Clamp to valid range
        }

        public async Task DeleteDrillHolesByProjectIdAsync(int projectId)
        {
            await _repository.DeleteByProjectIdAsync(projectId);
        }

        public async Task DeleteDrillHolesBySiteIdAsync(int projectId, int siteId)
        {
            await _repository.DeleteBySiteIdAsync(projectId, siteId);
        }

        public async Task<int> GetDrillHoleCountAsync()
        {
            return await _repository.GetCountAsync();
        }

        public async Task<int> GetDrillHoleCountByProjectIdAsync(int projectId)
        {
            return await _repository.GetCountByProjectIdAsync(projectId);
        }

        public async Task<int> GetDrillHoleCountBySiteIdAsync(int projectId, int siteId)
        {
            return await _repository.GetCountBySiteIdAsync(projectId, siteId);
        }
    }
}