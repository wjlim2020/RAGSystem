using System.ComponentModel.DataAnnotations;

public class UploadFileDto
{
    [Required]
    [AllowedExtensions(new string[] { ".txt", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt" , ".pdf"})]
    public IFormFile File { get; set; }
}
