using AutoMapper;
using leave_management.Contracts;
using leave_management.Data;
using leave_management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace leave_management.Controllers
{
    [Authorize]
    public class LeaveRequestController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly UserManager<Employee> _userManager;

        public LeaveRequestController(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            UserManager<Employee> userManager
            )
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _userManager = userManager;
        }

        [Authorize(Roles ="Administrator")]
        // GET: LeaveRequestController
        public async Task<ActionResult> Index()
        {
            var leaveRequests = await _unitOfWork.LeaveRequests.FindAll(
                includes: new List<string> { "RequestingEmployee", "LeaveType" });
           
            var leaveRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(leaveRequests);
            var model = new AdminLeaveRequestViewViewModel
            {
                TotalRequests = leaveRequestsModel.Count,
                ApprovedRequests = leaveRequestsModel.Count(q => q.Approved == true),
                PendingRequests = leaveRequestsModel.Count(q => q.Approved == null),
                RejectedRequests = leaveRequestsModel.Count(q => q.Approved == false),
                LeaveRequests = leaveRequestsModel
            };
            return View(model);
        }

        public async Task<ActionResult> MyLeave()
        {
            var employee = await _userManager.GetUserAsync(User);
            var employeeId = employee.Id;

            //var employeeAllocations = await _leaveAllocationRepo.GetLeaveAllocationsByEmployee(employeeId);
            var employeeAllocations = await _unitOfWork.LeaveAllocations.FindAll(q => q.EmployeeId == employeeId, 
                    includes: new List<string> { "LeaveType" });

            var employeeRequests = await _unitOfWork.LeaveRequests.FindAll(q => q.RequestingEmployeeId == employeeId);

            var employeeAllocationsModel = _mapper.Map<List<LeaveAllocationViewModel>>(employeeAllocations);
            var employeeRequestsModel = _mapper.Map<List<LeaveRequestViewModel>>(employeeRequests);

            var model = new EmployeeLeaveRequestViewViewModel
            {
                LeaveAllocations = employeeAllocationsModel,
                LeaveRequests = employeeRequestsModel
            };

            return View(model);
        }

        // GET: LeaveRequestController/Details/5
        public async Task<ActionResult> Details(int id)
        {
            var leaveRequest = await _unitOfWork.LeaveRequests.Find(q => q.Id == id,
                    includes: new List<string> { "RequestingEmployee", "LeaveType" });
                    //includes: new List<string> { "ApprovedBy", "RequestingEmployee", "LeaveType" });
            
            var model = _mapper.Map<LeaveRequestViewModel>(leaveRequest);

            return View(model);
        }

        public async Task<ActionResult> ApproveRequest(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var leaveRequest = await _unitOfWork.LeaveRequests.Find(q => q.Id == id);
                var employeeId = leaveRequest.RequestingEmployeeId;
                var leaveTypeId = leaveRequest.LeaveTypeId;
                var period = DateTime.Now.Year;
                var allocation = await _unitOfWork.LeaveAllocations.Find(q => q.EmployeeId == employeeId
                                                    && q.Period == period
                                                    && q.LeaveTypeId == leaveTypeId);
               
                var daysRequested = (int)(leaveRequest.EndDate - leaveRequest.StartDate).TotalDays;
                allocation.NumberOfDays = allocation.NumberOfDays - daysRequested;

                leaveRequest.Approved = true;
                leaveRequest.AprrovedById = user.Id;
           
                leaveRequest.DateActioned = DateTime.Now;

                _unitOfWork.LeaveRequests.Update(leaveRequest);
                _unitOfWork.LeaveAllocations.Update(allocation);
                await _unitOfWork.Save();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }
           
        }

        public async Task<ActionResult> RejectRequest(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var leaveRequest = await _unitOfWork.LeaveRequests.Find(q => q.Id == id);
                leaveRequest.Approved = false;
                leaveRequest.AprrovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _unitOfWork.LeaveRequests.Update(leaveRequest);
                await _unitOfWork.Save();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }

        }

        // GET: LeaveRequestController/Create
        public async Task<ActionResult> Create()
        {
            var leaveTypes = await _unitOfWork.LeaveTypes.FindAll();
            var leaveTypeItems = leaveTypes.Select(q => new SelectListItem
            {
                Text = q.Name,
                Value = q.Id.ToString()
            });
            var model = new CreateLeaveRequestViewModel
            {
                LeaveTypes = leaveTypeItems
            };
            return View(model);
        }

        // POST: LeaveRequestController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(CreateLeaveRequestViewModel model)
        {
            
            try
            {
                var startDate = Convert.ToDateTime(model.StartDate);
                var endDate = Convert.ToDateTime(model.EndDate);
                var leaveTypes = await _unitOfWork.LeaveTypes.FindAll();
                var employee = await _userManager.GetUserAsync(User);
                var period = DateTime.Now.Year;
                var allocation = await _unitOfWork.LeaveAllocations.Find(q => q.EmployeeId == employee.Id
                                                    && q.Period == period
                                                    && q.LeaveTypeId == model.LeaveTypeId);

                int daysRequested = (int)(endDate - startDate).TotalDays;

                var leaveTypeItems = leaveTypes.Select(q => new SelectListItem
                {
                    Text = q.Name,
                    Value = q.Id.ToString()
                });

                model.LeaveTypes = leaveTypeItems;

                if (allocation == null)
                {
                    ModelState.AddModelError("", "You Have No Days Left");
                }
                if (DateTime.Compare(startDate, endDate) > 1)
                {
                    ModelState.AddModelError("", "Start Date Cannot be further in the future than the End Date");
                }
                if (daysRequested > allocation.NumberOfDays)
                {
                    ModelState.AddModelError("", "You Do Not Have Sufficient Days For This Request");
                }
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                var leaveRequestModel = new LeaveRequestViewModel
                {
                    RequestingEmployeeId = employee.Id,
                    StartDate = startDate,
                    EndDate = endDate,
                    Approved = null,
                    DateRequested = DateTime.Now,
                    DateActioned = DateTime.Now,
                    LeaveTypeId = model.LeaveTypeId,
                    RequestComments = model.RequestComments
                };

                var leaveRequest = _mapper.Map<LeaveRequest>(leaveRequestModel);
                await _unitOfWork.LeaveRequests.Create(leaveRequest);
                await _unitOfWork.Save();


                return RedirectToAction("MyLeave");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Something went wrong...");
                return View(model);
            }
        }

        public async Task<ActionResult> CancelRequest(int id)
        {
            var leaveRequest = await _unitOfWork.LeaveRequests.Find(q => q.Id == id);
            leaveRequest.Cancelled = true;
            _unitOfWork.LeaveRequests.Update(leaveRequest);
            await _unitOfWork.Save();

            return RedirectToAction("MyLeave");
        }

        // GET: LeaveRequestController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: LeaveRequestController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: LeaveRequestController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: LeaveRequestController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _unitOfWork.Dispose();
            base.Dispose(disposing);
        }
    }
}
