class PaymentProcessor:
    def __init__(self, amount):
        self.amount = amount

    # 1. Protected/Internal Convention (Single Underscore)
    def _validate_funds(self):
        print("Internal check: Validating funds...")
        return True

    # 2. Strongly "Private" / Name Mangled (Double Underscore)
    def __execute_transfer(self):
        print(f"Executing transfer of ${self.amount}")

    # Public Method
    def process(self):
        if self._validate_funds():
            self.__execute_transfer() # Accessible inside the class
