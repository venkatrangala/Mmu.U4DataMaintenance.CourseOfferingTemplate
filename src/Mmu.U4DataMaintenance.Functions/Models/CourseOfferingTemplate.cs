namespace Mmu.U4DataMaintenance.Functions.Models
{
    public class CourseOfferingTemplate
    {
        public int Id { get; set; }
        public int MinEnrolled { get; set; }
        public int MaxEnrolled { get; set; }
        public int PriceGroupId { get; set; }
        public int CourseLevelId { get; set; }
        public int EnrollmentModeId { get; set; }
    }
}
