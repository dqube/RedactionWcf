# WCF Appointment Service - REST API Testing Guide

## Service Endpoint
```
http://localhost:[PORT]/AppointmentService.svc
```
Replace `[PORT]` with your IIS Express or development server port number.

## Available Operations

### 1. Get All Appointments
**Endpoint:** `GET /AppointmentService.svc/appointments`

**Example Request:**
```http
GET http://localhost:51234/AppointmentService.svc/appointments
Accept: application/json
```

**Example Response:**
```json
[
  {
    "Id": 1,
    "Title": "Annual Checkup",
    "Description": "Routine physical examination",
    "AppointmentDate": "2024-12-22T10:00:00",
    "Location": "Room 101",
    "PatientName": "John Doe",
    "DoctorName": "Dr. Smith",
    "DurationMinutes": 30
  },
  {
    "Id": 2,
    "Title": "Dental Cleaning",
    "Description": "Regular teeth cleaning",
    "AppointmentDate": "2024-12-29T14:00:00",
    "Location": "Room 205",
    "PatientName": "Jane Smith",
    "DoctorName": "Dr. Johnson",
    "DurationMinutes": 45
  }
]
```

---

### 2. Get Appointment by ID
**Endpoint:** `GET /AppointmentService.svc/appointments/{id}`

**Example Request:**
```http
GET http://localhost:51234/AppointmentService.svc/appointments/1
Accept: application/json
```

**Example Response:**
```json
{
  "Id": 1,
  "Title": "Annual Checkup",
  "Description": "Routine physical examination",
  "AppointmentDate": "2024-12-22T10:00:00",
  "Location": "Room 101",
  "PatientName": "John Doe",
  "DoctorName": "Dr. Smith",
  "DurationMinutes": 30
}
```

---

### 3. Create New Appointment
**Endpoint:** `POST /AppointmentService.svc/appointments`

**Example Request:**
```http
POST http://localhost:51234/AppointmentService.svc/appointments
Content-Type: application/json

{
  "Title": "Follow-up Consultation",
  "Description": "Follow-up appointment for test results review",
  "AppointmentDate": "2024-12-20T10:00:00",
  "Location": "Room 303",
  "PatientName": "Michael Johnson",
  "DoctorName": "Dr. Williams",
  "DurationMinutes": 30
}
```

**Example Response:**
```json
{
  "Id": 3,
  "Title": "Follow-up Consultation",
  "Description": "Follow-up appointment for test results review",
  "AppointmentDate": "2024-12-20T10:00:00",
  "Location": "Room 303",
  "PatientName": "Michael Johnson",
  "DoctorName": "Dr. Williams",
  "DurationMinutes": 30
}
```

---

## Testing with cURL

### Get All Appointments
```bash
curl -X GET "http://localhost:51234/AppointmentService.svc/appointments" -H "Accept: application/json"
```

### Get Appointment by ID
```bash
curl -X GET "http://localhost:51234/AppointmentService.svc/appointments/1" -H "Accept: application/json"
```

### Create New Appointment
```bash
curl -X POST "http://localhost:51234/AppointmentService.svc/appointments" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d "{\"Title\":\"Follow-up Consultation\",\"Description\":\"Follow-up appointment\",\"AppointmentDate\":\"2024-12-20T10:00:00\",\"Location\":\"Room 303\",\"PatientName\":\"Michael Johnson\",\"DoctorName\":\"Dr. Williams\",\"DurationMinutes\":30}"
```

---

## Testing with PowerShell

### Get All Appointments
```powershell
Invoke-RestMethod -Uri "http://localhost:51234/AppointmentService.svc/appointments" -Method Get -ContentType "application/json"
```

### Get Appointment by ID
```powershell
Invoke-RestMethod -Uri "http://localhost:51234/AppointmentService.svc/appointments/1" -Method Get -ContentType "application/json"
```

### Create New Appointment
```powershell
$appointment = @{
    Title = "Follow-up Consultation"
    Description = "Follow-up appointment for test results review"
    AppointmentDate = "2024-12-20T10:00:00"
    Location = "Room 303"
    PatientName = "Michael Johnson"
    DoctorName = "Dr. Williams"
    DurationMinutes = 30
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:51234/AppointmentService.svc/appointments" -Method Post -Body $appointment -ContentType "application/json"
```

---

## Validation Rules
- **Title** is required (cannot be empty or whitespace)
- **PatientName** is required (cannot be empty or whitespace)
- ID is auto-generated when creating appointments

## Error Responses
- **400 Bad Request**: When required fields are missing or invalid
- **404 Not Found**: When requesting an appointment that doesn't exist
