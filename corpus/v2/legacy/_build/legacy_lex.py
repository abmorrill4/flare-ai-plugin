"""Domain data for the synthetic legacy project (Marlowe Dispatch, a fictional field-service dispatch app)."""

# module key -> data.  'tasks' are (verb-key, title-template) pairs; {o} is the object noun phrase.
MODS = {
    'wo': dict(name='Work Orders', obj='work order', objs='work orders', menu='Work Orders', folder='WorkOrders',
               fields=['Customer', 'Site', 'Priority', 'Problem description', 'Scheduled date', 'Assigned technician'],
               tasks=['create', 'edit', 'delete', 'search', 'assign', 'close', 'reopen', 'duplicate', 'print', 'export', 'attach', 'bulk', 'priority', 'note']),
    'cust': dict(name='Customers', obj='customer record', objs='customer records', menu='Customers', folder='Customers',
                 fields=['Customer name', 'Phone', 'Billing address', 'Contract', 'Notes'],
                 tasks=['create', 'edit', 'merge', 'search', 'export', 'archive', 'restore', 'import', 'contact', 'contract']),
    'site': dict(name='Sites', obj='site', objs='sites', menu='Sites', folder='Sites',
                 fields=['Site name', 'Address', 'Access instructions', 'Equipment'],
                 tasks=['create', 'edit', 'equipment', 'search', 'delete', 'print']),
    'tech': dict(name='Technicians', obj='technician profile', objs='technician profiles', menu='Technicians', folder='Technicians',
                 fields=['Name', 'Skills', 'Home depot', 'Vehicle', 'Working hours'],
                 tasks=['create', 'edit', 'hours', 'skills', 'deactivate', 'reactivate', 'search', 'vehicle']),
    'sched': dict(name='Scheduling', obj='appointment', objs='appointments', menu='Schedule', folder='Scheduling',
                  fields=['Date', 'Start time', 'Duration', 'Technician', 'Work order'],
                  tasks=['book', 'move', 'cancel', 'calendar', 'conflict', 'recurring']),
    'route': dict(name='Routes', obj='route', objs='routes', menu='Routes', folder='Routes',
                  fields=['Route name', 'Date', 'Start depot', 'Stops'],
                  tasks=['plan', 'optimize', 'stop', 'print', 'send', 'delete']),
    'inv': dict(name='Invoices', obj='invoice', objs='invoices', menu='Invoices', folder='Invoices',
                fields=['Invoice number', 'Customer', 'Amount', 'Tax code', 'Due date'],
                tasks=['create', 'edit', 'send', 'void', 'payment', 'export', 'tax', 'print']),
    'parts': dict(name='Parts and Inventory', obj='part', objs='parts', menu='Inventory', folder='Inventory',
                  fields=['Part number', 'Description', 'Quantity', 'Supplier', 'Location'],
                  tasks=['create', 'edit', 'adjust', 'order', 'receive', 'search', 'import', 'delete']),
    'rep': dict(name='Reports', obj='report', objs='reports', menu='Reports', folder='Reports',
                fields=['Report name', 'Date range', 'Format', 'Recipients'],
                tasks=['run', 'schedule', 'export', 'custom', 'share', 'delete']),
    'mob': dict(name='Mobile App', obj='job', objs='jobs', menu='Jobs', folder='Mobile',
                fields=['Job number', 'Status', 'Notes', 'Photo', 'Signature'],
                tasks=['login', 'view', 'accept', 'status', 'signature', 'photo', 'sync', 'offline']),
    'adm': dict(name='Administration', obj='user account', objs='user accounts', menu='Admin', folder='Admin',
                fields=['User name', 'Email address', 'Role', 'Password', 'Business hours'],
                tasks=['create', 'edit', 'password', 'role', 'permissions', 'audit', 'backup', 'restore', 'email', 'hours']),
    'int': dict(name='Integrations', obj='connector', objs='connectors', menu='Integrations', folder='Integrations',
                fields=['Connector name', 'Server address', 'User name', 'Password', 'Field map'],
                tasks=['connect', 'map', 'test', 'synclog', 'disconnect', 'apikey']),
}

VERB_TITLE = {
    'create': 'Create {a} {o}', 'edit': 'Edit {a} {o}', 'delete': 'Delete {a} {o}', 'search': 'Search for {a} {o}', 'assign': 'Assign {a} {o} to a technician',
    'close': 'Close {a} {o}', 'reopen': 'Reopen {a} closed {o}', 'duplicate': 'Duplicate {a} {o}', 'print': 'Print {a} {o}', 'export': 'Export {os} to a file',
    'attach': 'Attach a file to {a} {o}', 'bulk': 'Update several {os} at once', 'priority': 'Change the priority of {a} {o}', 'note': 'Add a note to {a} {o}',
    'merge': 'Merge duplicate {os}', 'archive': 'Archive {a} {o}', 'restore': 'Restore {a} archived {o}', 'import': 'Import {os} from a spreadsheet',
    'contact': 'Add a contact person', 'contract': 'Set the service contract', 'equipment': 'Add equipment to a site', 'hours': 'Set working hours',
    'skills': 'Set skills', 'deactivate': 'Deactivate {a} {o}', 'reactivate': 'Reactivate {a} {o}', 'vehicle': 'Assign a vehicle',
    'book': 'Book {a} {o}', 'move': 'Move {a} {o}', 'cancel': 'Cancel {a} {o}', 'calendar': 'View the calendar', 'conflict': 'Resolve a double booking',
    'recurring': 'Set up recurring {os}', 'plan': 'Plan {a} {o}', 'optimize': 'Optimize {a} {o}', 'stop': 'Edit a stop on {a} {o}', 'send': 'Send {a} {o} to the mobile app',
    'void': 'Void {a} {o}', 'payment': 'Record a payment', 'tax': 'Apply a tax code', 'adjust': 'Adjust stock quantity', 'order': 'Order {os} from a supplier',
    'receive': 'Receive {os} into stock', 'run': 'Run {a} {o}', 'schedule': 'Schedule {a} {o}', 'custom': 'Create a custom {o}', 'share': 'Share {a} {o}',
    'login': 'Log in on a mobile device', 'view': 'View your {os}', 'accept': 'Accept {a} {o}', 'status': 'Update the status of {a} {o}',
    'signature': 'Capture a customer signature', 'photo': 'Take a photo', 'sync': 'Synchronize the mobile app', 'offline': 'Work offline',
    'password': 'Reset a password', 'role': 'Set a role', 'permissions': 'Set permissions', 'audit': 'Review the audit log', 'backup': 'Back up the database',
    'restore_db': 'Restore a backup', 'email': 'Configure email', 'connect': 'Connect {a} {o}', 'map': 'Map fields', 'test': 'Test the connection',
    'synclog': 'View the synchronization log', 'disconnect': 'Disconnect {a} {o}', 'apikey': 'Create an API key',
}

# Intro-sentence frames.  {Menu}, {o}, {os}, {v} (infinitive), {ing} are filled in.  Plain entries are clean;
# entries with a tag are written to contain exactly one planted defect: (text, kind, bad, fix).
INTROS_CLEAN = [
    'Use the {Menu} screen to {v} {a} {o}.',
    'Dispatchers {v} {a} {o} on the {Menu} screen.',
    'You can {v} {a} {o} from the {Menu} screen.',
    'Follow these steps to {v} {a} {o}.',
]
INTROS_BAD = [
    ('Dispatchers {B} the {Menu} screen to {v} {a} {o}.', 'utilize', 'utilize', 'use'),
    ('{B} {v} {a} {o}, open the {Menu} screen.', 'in_order_to', 'In order to', 'To'),
    ('{B} {ing} {a} {o}, confirm that the customer record exists.', 'prior_to', 'Prior to', 'Before'),
    ('A dispatcher {B} {v} {a} {o} from the {Menu} screen.', 'is_able_to', 'is able to', 'can'),
    ('{B} that you need the Dispatcher role to {v} {a} {o}.', 'please_note', 'Please note', 'Note'),
    ('{B} open the {Menu} screen and {v} {a} {o}.', 'simply', 'Simply', ''),
    ('{B} that you review each {o} before you save it.', 'we', 'We recommend', 'Review'),
    ('The {o} {B} appear in the list after you save it.', 'will', 'will', ''),
    ('{B} the {Menu} screen to {v} {a} {o}.', 'the_user', 'The user opens', 'You open'),
]
NOTES_CLEAN = [
    'Your changes are saved when you click {Save}.',
    'Only dispatchers and administrators can see this screen.',
    'The list updates every 30 seconds.',
    'You cannot undo this action after you save.',
    'Fields marked with an asterisk are required.',
    'The screen remembers the last filter that you used.',
    'If the program shows the message "The record is locked", wait a minute and try again.',
    'If you see "No technician available", pick another time.',
]
NOTES_BAD = [
    ('Add a short reason, {B} the cause of the delay.', 'latin', 'e.g.', 'for example,'),
    ('{B} do not close the window until the progress bar finishes.', 'please', 'Please', ''),
    ('Your changes are saved{B}', 'exclaim', '!', '.'),
    ('Configure the other options {B}.', 'vague', 'as appropriate for your situation', 'by setting each value'),
    ('The {o} {B} when you click Save.', 'passive', 'is saved by the system', 'is saved'),
    ('{B}, the {o} is locked until you save.', 'wordy', 'At this point in time', 'Now'),
]

UI_BUTTONS = ['Save', 'Cancel', 'OK', 'Apply', 'Close', 'Next', 'Back', 'Delete', 'Edit', 'New', 'Print', 'Export', 'Import', 'Search', 'Refresh', 'Send', 'Assign']

FILLERS = ['Customer', 'Priority', 'Description', 'Status', 'Notes', 'Date']

TROUBLE = [
    ('The list is empty after I search', 'Clear the filters, and then search again. Filters stay active until you click Clear.'),
    ('The Save button is dimmed', 'You do not have permission to change this record. Ask an administrator to give you the Dispatcher role.'),
    ('A technician does not see a job', 'Check that the job is assigned and that the technician has synchronized the mobile app.'),
    ('An invoice shows the wrong tax', 'Open the invoice and check the tax code for the customer. The tax code on the site overrides the customer tax code.'),
    ('The export file opens with strange characters', 'Open the file as UTF-8 text, or choose the Excel format when you export.'),
    ('Two appointments overlap', 'Open the calendar and move one appointment. The conflict marker disappears after you save.'),
    ('The mobile app says it is offline', 'Check the mobile connection. Jobs that you update offline are sent when the connection returns.'),
    ('A report shows no data', 'Check the date range. Reports include only completed jobs unless you choose All statuses.'),
]


OBJ_OVERRIDE = {('adm', 'email'): ('mail setting', 'mail settings'), ('adm', 'backup'): ('backup', 'backups'), ('adm', 'restore'): ('backup', 'backups'), ('adm', 'audit'): ('audit log entry', 'audit log entries'),
                ('adm', 'hours'): ('business hour', 'business hours'), ('adm', 'role'): ('role', 'roles'), ('adm', 'permissions'): ('permission', 'permissions'), ('adm', 'password'): ('password', 'passwords'),
                ('tech', 'hours'): ('working hour', 'working hours'), ('tech', 'skills'): ('skill', 'skills'), ('tech', 'vehicle'): ('vehicle', 'vehicles'), ('int', 'map'): ('field map', 'field maps'),
                ('int', 'test'): ('connection', 'connections'), ('int', 'synclog'): ('log entry', 'log entries'), ('int', 'apikey'): ('API key', 'API keys'), ('cust', 'contact'): ('contact person', 'contact persons'),
                ('cust', 'contract'): ('service contract', 'service contracts'), ('site', 'equipment'): ('equipment item', 'equipment items'), ('inv', 'payment'): ('payment', 'payments'), ('inv', 'tax'): ('tax code', 'tax codes'),
                ('parts', 'adjust'): ('stock quantity', 'stock quantities'), ('sched', 'calendar'): ('calendar', 'calendars'), ('sched', 'conflict'): ('double booking', 'double bookings'), ('mob', 'signature'): ('signature', 'signatures'),
                ('mob', 'photo'): ('photo', 'photos'), ('mob', 'sync'): ('sync', 'syncs'), ('mob', 'offline'): ('offline job', 'offline jobs'), ('mob', 'login'): ('login', 'logins')}


# Sentences that use the competing term forms ({wo} work order, {tech} technician, {cust} customer, {login}, {email}).
CONTEXT = [
    'The {cust} receives an {email} when the {wo} is saved.',
    'Ask the {tech} to {login} to the mobile app before the visit.',
    'If the {cust} calls, find the {wo} by the phone number.',
    'Each {tech} needs a personal user name to {login}.',
    'Send the {cust} an {email} if the visit time changes.',
    'A {tech} sees only the {wos} that are assigned to that person.',
    'The {cust} can ask for a copy of the {wo} by {email}.',
    'Dispatchers {login} each morning and check the {wos} for the day.',
    'Give the {tech} the {wo} number before the visit.',
    'Do not give the {cust} the password for the {email} account.',
    'The {tech} must {login} again after a long break.',
    'Check the {cust} {email} address before you send the {wo}.',
]
