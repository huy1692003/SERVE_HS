﻿using API_HomeStay_HUB.Realtime;
using API_HomeStay_HUB.Data;
using API_HomeStay_HUB.Helpers;
using API_HomeStay_HUB.Model;
using API_HomeStay_HUB.Repositories.Interfaces;
using API_HomeStay_HUB.Services;
using API_HomeStay_HUB.Services.Interface;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API_HomeStay_HUB.Repositories
{
    public class BookingRepository : IBookingRepository
    {
        private readonly DBContext _dbContext;
        private readonly ISendMaillService _sendM;
        private readonly IHubContext<MyHub> _hub;
        public BookingRepository(DBContext dBContext, ISendMaillService sendMaill , IHubContext<MyHub> hub)
        {
            _dbContext = dBContext;
            _sendM = sendMaill;
            this._hub = hub;
        }
        private int GenerateRandomId()
        {
            DateTime now = TimeHelper.GetDateTimeVietnam();
            // Lấy mili giây từ thời gian hiện tại và chuyển đổi nó thành số nguyên
            return (int)(now.Ticks % int.MaxValue); // Sử dụng modulo để đảm bảo trong giới hạn int
        }
        public async Task<bool> createBooking(Booking booking)
        {
           
            booking.BookingID = GenerateRandomId();
            if (!await checkDateExitedBooking(booking.HomeStayID!, booking.CheckInDate!, booking.CheckOutDate!))
            {
                booking.status = 1;
                booking.BookingTime = TimeHelper.GetDateTimeVietnam();
                var entity= await _dbContext.AddAsync(booking);
                var check = await _dbContext.SaveChangesAsync() > 0;
                if (check)
                {
                    var owner = await _dbContext.OwnerStays.FirstOrDefaultAsync(s => s.OwnerID == booking.OwnerID);
                    await _hub.Clients.All.SendAsync("RefeshDateHomeStay", entity.Entity.HomeStayID);
                    var notification = new Notification {
                        UserID = owner.UserID,
                        Title = "Thông báo đơn đặt phòng mới",
                        Message = $"Homestay có mã #{booking.HomeStayID} vừa có khách hàng mới đã đặt phòng vào " + TimeHelper.formatDateVN(booking.BookingTime),
                        CreatedAt = TimeHelper.GetDateTimeVietnam(),
                        IsRead =false,
                        Type="success"


                    };
                    _dbContext.Notifications.Add(notification); 
                    _dbContext.SaveChanges();   
                    await _hub.Clients.All.SendAsync("ReseiverBookingNew", booking.OwnerID ,notification );
                    
                    return true;
                }
                return false;
            }
            else
            {
                return false;
            }
        }

        public async Task<IEnumerable<dynamic>> getBookingDates(int idHomeStay)
        {
            return await _dbContext.Bookings.
                Where(b => b.HomeStayID == idHomeStay && b.IsCancel!=1).
                Select(b => new { b.CheckInDate, b.CheckOutDate }).ToListAsync();
        }
        

        public async Task<bool> confirmBooking(int idBooking)
        {
            try
            {
                var booking = await _dbContext.Bookings.FirstOrDefaultAsync(s => s.BookingID == idBooking);
                if (booking != null)
                {
                    booking.IsConfirm = 1;
                    booking.status = 2;
                    booking.TimeConfirm = TimeHelper.GetDateTimeVietnam();
                    await _dbContext.BookingProcesses.AddAsync
                        (
                            new BookingProcess
                            {
                                BookingID = idBooking,

                                StepOrder = 0,


                            }
                        );
                    bool checkConfirm = await _dbContext.SaveChangesAsync() > 0;
                    if (checkConfirm)
                    {
                       return await sendMaill_ConfirmBK(booking);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {


                return false;
            }
        }

        public async Task<bool> sendMaill_ConfirmBK(Booking booking)
        {
            // Lấy thông tin homestay từ database
            var homeStay = await _dbContext.HomeStays.FirstOrDefaultAsync(s => s.HomestayID == booking.HomeStayID);
            if (homeStay == null)
            {
                Console.WriteLine("Homestay không tồn tại.");
                return false;
            }

            // Lấy thông tin khách hàng
            var emailUser = booking.Email;
            var nameUser = booking.Name;

            // Tiêu đề email
            string titleSendMail = "Thông báo đơn đặt phòng đã được xác nhận";
            var inforOwner = await _dbContext.OwnerStays.
                Join(_dbContext.Users ,
                owner=>owner.UserID , 
                user=>user.UserID ,
                (owner, user) => new{ Owner=owner, User=user }).FirstOrDefaultAsync(s => s.Owner.OwnerID == booking.OwnerID);
            
            // Nội dung email
            var content = $@"
                    <h2>Xin chào {nameUser},</h2>
                    <p>Chúc mừng bạn! Đơn đặt phòng của bạn tại <b>{homeStay.HomestayName}</b> đã được xác nhận thành công.</p>
                    <h3>Thông tin đặt phòng:</h3>
                    <ul>
                        <li><b>Địa chỉ Homestay:</b> {homeStay.AddressDetail}, {homeStay.WardOrCommune}, {homeStay.District}, {homeStay.Province}, {homeStay.Country}</li>
                        <li><b>Ngày nhận phòng:</b> {booking.CheckInDate.ToString("dd/MM/yyyy")}</li>
                        <li><b>Ngày trả phòng:</b> {booking.CheckOutDate.ToString("dd/MM/yyyy")}</li>
                        <li><b>Số lượng khách:</b> {booking.NumberOfGuests} người</li>
                        <li><b>Tổng giá trị:</b> {booking.TotalPrice:C}</li>
                        <li><b>Phương thức thanh toán:</b> {booking.PaymentMethod}</li>
                    </ul>
                    <p>Địa chỉ Homestay trên bản đồ: <a href='{homeStay.LinkGoogleMap}'>Xem trên Google Maps</a></p>
                    <p>Đây là thông tin của gia chủ bạn hãy liên hệ khi cần thiết :<span>{inforOwner.User.FullName}</span> - Số điện thoại :<span>{inforOwner.User.PhoneNumber}</span> </p>
                    <p>Cảm ơn bạn đã sử dụng dịch vụ của chúng tôi!</p>
                    <p>Trân trọng,</p>
                    <p><b>Đội ngũ hỗ trợ HomeStay HUB</b></p>
                ";

            // Gửi email
            try
            {
                var sendMailService = new SendMaillService();
                await sendMailService.SendMaill(emailUser, titleSendMail, content);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gửi email thất bại: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> sendMaill_CancelBK(Booking booking)
        {
            // Lấy thông tin homestay từ database
            var homeStay = await _dbContext.HomeStays.FirstOrDefaultAsync(s => s.HomestayID == booking.HomeStayID);
            if (homeStay == null)
            {
                Console.WriteLine("Homestay không tồn tại.");
                return false;
            }

            // Lấy thông tin khách hàng
            var emailUser = booking.Email;
            var nameUser = booking.Name;

            // Tiêu đề email
            string titleSendMail = "Thông báo đơn đặt phòng đã hủy";

            // Nội dung email
            var content = $@"
                    <h2>Xin chào {nameUser},</h2>
                    <p>Đơn đặt phòng của bạn tại <b>{homeStay.HomestayName}</b> đã được hủy.</p>
                    <h3>Thông tin đặt phòng đã hủy:</h3>
                    <ul>
                        <li><b>Địa chỉ Homestay:</b> {homeStay.AddressDetail}, {homeStay.WardOrCommune}, {homeStay.District}, {homeStay.Province}, {homeStay.Country}</li>
                        <li><b>Ngày nhận phòng:</b> {booking.CheckInDate.ToString("dd/MM/yyyy")}</li>
                        <li><b>Ngày trả phòng:</b> {booking.CheckOutDate.ToString("dd/MM/yyyy")}</li>
                        <li><b>Số lượng khách:</b> {booking.NumberOfGuests} người</li>
                        <li><b>Lý do hủy:</b> {booking.ReasonCancel}</li>
                    </ul>
                    <p>Nếu bạn có bất kỳ thắc mắc nào, vui lòng liên hệ với chúng tôi.</p>
                    <p>Trân trọng,</p>
                    <p><b>Đội ngũ hỗ trợ HomeStay HUB</b></p>
                ";

            // Gửi email
            try
            {
                var sendMailService = new SendMaillService();
                await sendMailService.SendMaill(emailUser, titleSendMail, content);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gửi email thất bại: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> cancelBooking(int idBooking, string reasonCancel)
        {
            var booking = await _dbContext.Bookings.FirstOrDefaultAsync(s => s.BookingID == idBooking);
            if (booking != null)
            {
                booking.IsCancel = 1;
                booking.status = -1;
                booking.ReasonCancel = reasonCancel;
                bool checkCancel = await _dbContext.SaveChangesAsync() > 0;
                if (checkCancel)
                {
                    return await sendMaill_CancelBK(booking);
                }
            }
            return false;
        }
        public async Task<bool> checkDateExitedBooking(int? idHomestay, DateTime? dateIn, DateTime? dateOut)
        {
            var bookingByHomeStays = await _dbContext.Bookings
                .Where(bk => bk.HomeStayID == idHomestay &&
                             dateIn < bk.CheckOutDate && dateOut > bk.CheckInDate)
                .ToListAsync();

            return bookingByHomeStays.Any();
        }


    }
}
