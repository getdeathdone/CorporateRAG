namespace CorporateRag.Rag;

public interface IRagService
{
    Task IndexPdfAsync(string pdfPath, CancellationToken cancellationToken = default);
    Task<string> AskAsync(string question, CancellationToken cancellationToken = default);
}
