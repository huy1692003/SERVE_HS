﻿using API_HomeStay_HUB.Data;
using API_HomeStay_HUB.Model;
using API_HomeStay_HUB.Repositories.Intefaces;
using Microsoft.EntityFrameworkCore;
using System;


namespace API_HomeStay_HUB.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly DBContext _context;

        public UserRepository(DBContext _context)
        {
            this._context = _context;

        }
        public async Task<User?> loginUser(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(s => s.Username == username && s.TypeUser < 2);
            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                var cus=_context.Customers.FirstOrDefault(c=>c.UserID==user.UserID);
                user.Customer = cus!;
                if(user.TypeUser==1)
                {
                    var ownerStay = _context.OwnerStays.FirstOrDefault(c => c.UserID == user.UserID);
                    user.OwnerStay = ownerStay!;
                }

                return user;
            }
            return null;
        }
        public async Task<User?> loginAdmin(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(s => s.Username == username && s.TypeUser == 2);
            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                var admin = await _context.Administrators.FirstOrDefaultAsync(a => a.UserID == user.UserID);
                user.Administrator = admin!;
                return user;
            }
            return null;
        }

        public async Task<bool> addUser(User user, int typeUser)
        {
            var userDB = await _context.Users.SingleOrDefaultAsync(u => u.Username == user.Username);
            if (userDB == null)
            {

                string userID_guid = Guid.NewGuid().ToString();
                user.UserID = userID_guid;
                user.Status = 1;
                user.TypeUser = typeUser;
                user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                await _context.Users.AddAsync(user);
                bool isCreated = await _context.SaveChangesAsync() > 0;

                if (isCreated)
                {
                    string guid = Guid.NewGuid().ToString();
                    if (typeUser == 2)
                    {
                        await _context.Administrators.AddAsync(new Administrator { AdminID = guid, UserID = userID_guid });
                    }
                    else
                    {
                        await _context.Customers.AddAsync(new Customer { CusID = guid, UserID = userID_guid });
                    }

                    // Lưu các thay đổi vào cơ sở dữ liệu
                    return await _context.SaveChangesAsync() > 0;
                }
                else
                {
                    return false;
                }
            }
            return false;


        }

        public async Task<bool> changePassWord(string userID, string passOld, string passNew)
        {
            var user = await _context.Users.FindAsync(userID);
            if (user == null)
            {
                return false;
            }
            else
            {
                if (BCrypt.Net.BCrypt.Verify(passOld, user.Password))
                {
                    user.Password = BCrypt.Net.BCrypt.HashPassword(passNew);
                    return await _context.SaveChangesAsync() > 0;
                }
                return false;

            }


        }
        public async Task<bool> updateProfile(User user)
        {
            var _user = await _context.Users.FindAsync(user.UserID);
            if (user == null)
            {
                return false;
            }
            else
            {
                _context.Users.Update(user);
                return _context.SaveChanges() > 0;
            }
        }
        public async Task<bool> lockUser(User user)
        {
            return false;
        }
    }
}
